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
        [InlineData(".equ A")]
        [InlineData(".equ A,")]
        [InlineData(".equ A, label0")]
        [InlineData(".equ 1A, 5")]
        [InlineData(".equ mov, 1")]
        [InlineData(".equ r1, 5")]
        [InlineData(".include uart.rasm")]
        public void BadDirectiveLineIsReported(string declaration)
        {
            //the grammar accepts each directive whole or reports one message: file, line and
            //the token the line started with, naming the two accepted forms
            using var src = new TempSource();
            string main = src.Write("main.rasm", declaration + "\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage($"{main}:1: preprocessor:*");
        }

        [Theory]
        [InlineData(".equ A, 2 + 3", 5)]
        [InlineData(".equ A, 10 - 4", 6)]
        [InlineData(".equ A, 2 * 3", 6)]
        [InlineData(".equ A, 10 / 2", 5)]
        [InlineData(".equ A, (7)", 7)]
        [InlineData(".equ A, 1 + 2 * 3", 7)]
        [InlineData(".equ A, 6 / 3 / 2", 1)]
        [InlineData(".equ A, 100 - 20 - 5", 75)]
        public void ConstantValueMayBeANumberExpression(string declaration, int expected)
        {
            //the preprocessor runs the same number-expression rules as the parser, so a constant
            //may be spelled as an expression; the engine tries the multiply/divide rule before
            //add/subtract, so '1 + 2 * 3' reduces as 1 + (2 * 3)
            using var src = new TempSource();
            string main = src.Write("main.rasm", $"{declaration}\n    movi r1, A\n");
            string reference = src.Write("reference.rasm", $"    movi r1, {expected}\n");

            Code(main).Should().Be(Code(reference));
        }

        [Fact]
        public void ExpressionMeansTheSameInAConstantAndInline()
        {
            //the expression rules of the two passes are one shared set (Asm/Expressions.cs):
            //a constant defined by an expression and the same expression written in place
            //must assemble to the same words
            using var src = new TempSource();
            string viaConstant = src.Write("constant.rasm", ".equ A, 6 / 3 / 2\n    dw A\n");
            string viaExpression = src.Write("expression.rasm", "    dw 6 / 3 / 2\n");

            Words(viaConstant).Should().Equal(Words(viaExpression));
        }

        [Fact]
        public void DivisionByZeroIsReported()
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", ".equ A, 1 / 0\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage("*Division by zero in '1 / 0'*");
        }

        [Fact]
        public void UnknownDirectiveIsReported()
        {
            using var src = new TempSource();
            string main = src.Write("main.rasm", ".equiv A, 1\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage($"{main}:1: preprocessor:*'.equiv'*");
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

        [Fact]
        public void UnknownDirectiveInAnIncludedFilePointsToThatFile()
        {
            //the directive rules run while the file is spliced in, so the preprocessor already
            //knows which file and line the unknown '.name' stands in
            using var src = new TempSource();
            string lib = src.Write("lib/defs.rasm", "    nop\n.equiv A, 1\n");
            string main = src.Write("main.rasm", ".include \"lib/defs.rasm\"\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage($"{lib}:2: preprocessor:*'.equiv'*");
        }

        [Fact]
        public void FuncLineIsLeftToTheParserAndAssembler()
        {
            //'.func' is not a preprocessor directive: the line passes through the splicing as
            //ordinary code, the parser rule for TK.FUNC marks it, and the assembler aligns the
            //entry with an alignment NOP in front of it
            using var src = new TempSource();
            string main = src.Write("main.rasm", "    movi r1, 65\n    call f\n    reti\n.func f:\n    ret\n");

            var pre = new Preprocessor();
            pre.Process(main);

            //.func is not a grammar rule of the preprocessor, the line is untouched there
            pre.Lines.Single(l => l.Line.Contains(".func")).ParsedInstruction.Should().BeNull();

            var asm = new Assembler(0);
            asm.Compile(main);
            var funcLine = asm.Src.Single(l => l.Line.Contains(".func"));
            funcLine.ParsedInstruction!.IsFunc.Should().BeTrue();
            funcLine.Address.Should().Be(8);//the ';word align' NOP padded the entry up to a word boundary
            //movi + call + reti + NOP align + ret
            Words(main).Should().HaveCount(5);
        }

        [Fact]
        public void DirectiveThatDoesNotOpenItsLineIsASyntaxError()
        {
            //the directive rules only fire on a '.name' standing at the start of the line, so a
            //directive glued to code is left to the parser, which reports it as an unexpected token
            using var src = new TempSource();
            string main = src.Write("main.rasm", "    nop .equ A, 1\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage("*Syntax error*");
        }

        [Fact]
        public void BrokenSyntaxIsReportedOnceByTheParser()
        {
            //the preprocessor tokenizes every line to look for a directive; a line the tokenizer
            //cannot read must still be reported by the parser alone, with a single file:line prefix
            using var src = new TempSource();
            string main = src.Write("main.rasm", "    mov r1, \"unterminated\n");

            Action act = () => Words(main);
            act.Should().Throw<Exception>().WithMessage($"{main}:1: Syntax error*unterminated string literal");
        }
    }
}
