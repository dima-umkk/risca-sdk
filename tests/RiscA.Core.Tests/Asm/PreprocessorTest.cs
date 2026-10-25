using RiscA.Core.Asm;
using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RiscA.Core.Tests.Asm
{
    public class PreprocessorTest
    {
        //.include and .equ work on files, so every test writes its sources into a
        //throw away directory under the system temp path
        class TempSource : IDisposable
        {
            public string Dir { get; } = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "risca-asm-" + Guid.NewGuid().ToString("N"));

            public string Write(string relativePath, string content)
            {
                string full = Full(relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
                return full;
            }

            public string Full(string relativePath)
                => Path.Combine(Dir, relativePath.Replace('/', Path.DirectorySeparatorChar));

            public void Dispose()
            {
                try { Directory.Delete(Dir, true); }
                catch (IOException) { }
            }
        }

        //every word the assembler writes out for this file
        static List<ushort> Words(string filename)
        {
            Assembler asm = new(0);
            asm.Compile(filename);
            var words = new List<ushort>();
            foreach (var srcline in asm.Src)
                if (srcline.ParsedInstruction is ParsedInstruction pi)
                    foreach (Instruction instr in pi.Instructions)
                        words.Add(instr.Raw);
            return words;
        }

        //same as a single string, so the order of the words is checked as well
        static string Code(string filename)
            => string.Join(" ", Words(filename).Select(w => w.ToString("X4")));

        [Fact]
        public void ConstantsCanBeUsedWhereverANumberIsExpected()
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", """
                .equ OFF, 5
                    movi r1, OFF
                    add r1, OFF
                    ldw r2, [r3+OFF]
                    stw [r3+OFF], r1
                    dw OFF
                    db OFF
                """);
            string reference = src.Write("reference.rasm", """
                    movi r1, 5
                    add r1, 5
                    ldw r2, [r3+5]
                    stw [r3+5], r1
                    dw 5
                    db 5
                """);

            Code(main).Should().Be(Code(reference));
        }

        [Theory]
        [InlineData(".equ A, 200", 200)]
        [InlineData(".equ A, 0xC8", 200)]
        [InlineData(".equ A, 0b11001000", 200)]
        [InlineData(".equ A, 0x0F", 15)]
        [InlineData(".equ A, 0b101", 5)]
        [InlineData(".equ A, 0", 0)]
        public void ConstantValuesCanBeDecimalHexOrBinary(string declaration, int expected)
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", $"{declaration}\n    movi r1, A\n");
            string reference = src.Write("reference.rasm", $"    movi r1, {expected}\n");

            Code(main).Should().Be(Code(reference));
        }

        [Fact]
        public void ConstantMayHoldAFull32BitValue()
        {
            //device bases are 32 bit addresses: a constant may hold the whole number even though
            //a single instruction cannot take it, dw is 32 bit wide
            using var src = new TempSource();
            string main = src.Write("main.rasm", ".equ BASE, 0x00010000\n    dw BASE\n");
            string reference = src.Write("reference.rasm", "    dw 0x00010000\n");

            Code(main).Should().Be(Code(reference));
        }

        [Fact]
        public void NegativeConstantWorksAsBranchOffset()
        {
            //label0 is the instruction two bytes in front of the branch: a label line takes no
            //space, so the backward branch needs offset -1, the same way in both spellings
            using var src = new TempSource();
            string main = src.Write("main.rasm", ".equ BACK, -1\nlabel0:\n    nop\n    beqz r0, BACK\n");
            string reference = src.Write("reference.rasm", "label0:\n    nop\n    beqz r0, label0\n");

            Code(main).Should().Be(Code(reference));
            Words(main).Should().HaveCount(2);
        }

        [Fact]
        public void ConstantsFromAnIncludedLibraryAreVisibleInTheWholeProgram()
        {
            //the library is included at the end of the file (address 0 is the start of the
            //program), its constants must still be usable by the code above the include
            using var src = new TempSource();
            src.Write("lib/consts.rasm", ".equ UART_TX_DATA, 2\n");
            string main = src.Write("main.rasm", "    stw [r7+UART_TX_DATA], r1\n.include \"lib/consts.rasm\"\n");
            string reference = src.Write("reference.rasm", "    stw [r7+2], r1\n");

            Code(main).Should().Be(Code(reference));
        }

        [Fact]
        public void IncludeSplicesLibraryCodeAndLabelsResolveAcrossTheInclude()
        {
            using var src = new TempSource();
            src.Write("lib/putc.rasm", ".func putc:\n    nop\n    ret\n");
            string main = src.Write("main.rasm", "    movi r1, 65\n    call putc\n    reti\n.include \"lib/putc.rasm\"\n");
            string reference = src.Write("reference.rasm", "    movi r1, 65\n    call putc\n    reti\n.func putc:\n    nop\n    ret\n");

            Code(main).Should().Be(Code(reference));
        }

        [Fact]
        public void IncludedFileIsSplicedInOnlyOnce()
        {
            //the same library reached through two chains and directly must produce its code once,
            //otherwise a library would be duplicated and its labels defined twice
            using var src = new TempSource();
            src.Write("lib/common.rasm", "    movi r1, 7\n");
            src.Write("a.rasm", ".include \"lib/common.rasm\"\n");
            src.Write("b.rasm", ".include \"lib/common.rasm\"\n");
            string main = src.Write("main.rasm", ".include \"a.rasm\"\n.include \"b.rasm\"\n.include \"lib/common.rasm\"\n");
            string reference = src.Write("reference.rasm", ".include \"lib/common.rasm\"\n");

            Code(main).Should().Be(Code(reference));
            Words(main).Should().HaveCount(1);
        }

        [Fact]
        public void IncludedFileResolvesItsOwnIncludesRelativeToItself()
        {
            using var src = new TempSource();
            src.Write("lib/deep/inner.rasm", "    movi r2, 3\n");
            src.Write("lib/outer.rasm", ".include \"deep/inner.rasm\"\n");
            string main = src.Write("main.rasm", ".include \"lib/outer.rasm\"\n");
            string reference = src.Write("reference.rasm", "    movi r2, 3\n");

            Code(main).Should().Be(Code(reference));
        }

        [Fact]
        public void IncludeCycleDoesNotHang()
        {
            using var src = new TempSource();
            src.Write("a.rasm", ".include \"b.rasm\"\n    nop\n");
            src.Write("b.rasm", ".include \"a.rasm\"\n    movi r1, 1\n");
            string main = src.Write("main.rasm", ".include \"a.rasm\"\n    reti\n");
            string reference = src.Write("reference.rasm", "    movi r1, 1\n    nop\n    reti\n");

            Code(main).Should().Be(Code(reference));
        }

        [Fact]
        public void MissingIncludeFileIsReported()
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", ".include \"lib/absent.rasm\"\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage("*file not found 'lib/absent.rasm'*");
        }

        [Fact]
        public void ConstantCannotBeRedefined()
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", ".equ A, 1\n.equ A, 2\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage("*'A' is already defined at*");
        }

        [Theory]
        [InlineData(".equ mov, 1")]
        [InlineData(".equ r1, 5")]
        [InlineData(".equ nop, 0")]
        public void InstructionAndRegisterNamesCannotBeConstants(string declaration)
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", declaration + "\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage("*is an instruction or register name*");
        }

        [Theory]
        [InlineData(".equ A", "has no value")]
        [InlineData(".equ A,", "has no value")]
        [InlineData(".equ A, 1 + 2", "must be a number")]
        [InlineData(".equ A, label0", "must be a number")]
        [InlineData(".equ 1A, 5", "must start with a letter")]
        public void BadConstantDeclarationIsReported(string declaration, string expectedMessage)
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", declaration + "\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage($"*{expectedMessage}*");
        }

        [Fact]
        public void UnknownDirectiveIsReported()
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", ".equiv A, 1\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage("*unknown directive '.equiv'*");
        }

        [Fact]
        public void ConstantWithTheSameNameAsALabelIsReported()
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", ".equ FLAG, 1\nFLAG:\n    nop\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage("*'FLAG' is a .equ constant, it cannot be used as a label*");
        }

        [Fact]
        public void ConstantIsStillCheckedAgainstImmediateLimits()
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", ".equ BIG, 300\n    movi r1, BIG\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage("*Immediate must be in range 0 .. 255*");
        }

        [Fact]
        public void ErrorInAnIncludedFilePointsToThatFile()
        {
            using var src = new TempSource();
            string lib = src.Write("lib/bad.rasm", "    nop\n    mov r1 r2\n");
            string main = src.Write("main.rasm", ".include \"lib/bad.rasm\"\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage($"{lib}:2:*");
        }
    }
}
