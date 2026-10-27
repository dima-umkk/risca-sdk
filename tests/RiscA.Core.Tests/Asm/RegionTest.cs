using RiscA.Core.Asm;
using RiscA.Core.HW;
using RiscA.Core.Tests.HW;
using System;
using System.IO;
using System.Linq;

namespace RiscA.Core.Tests.Asm;

//The region check of doc/assembler-architecture.md, 7.2 (milestone A2.1): named regions are
//recognized and their prohibitions reported before parsing; physical regions are untouched.
public class RegionTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "risca-region-" + Guid.NewGuid().ToString("N"));

    public RegionTest()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Write(string source)
    {
        var file = Path.Combine(_dir, "prog.rasm");
        File.WriteAllText(file, source);
        return file;
    }

    private Assembler Compile(string source)
    {
        var asm = new Assembler(0);
        asm.Compile(Write(source));
        return asm;
    }

    private void AssertError(string source, string messagePattern)
    {
        var asm = new Assembler(0);
        var file = Write(source);

        Action act = () => asm.Compile(file);

        act.Should().Throw<Exception>().WithMessage(messagePattern);
    }

    [Fact]
    public void DotEndClosesTheRegionWithoutProducingCode()
    {
        var asm = Compile(".func f:\nnop\nret\n.end\nreti\n");

        var end = asm.Src.Single(l => l.Line.Trim() == ".end");
        end.ParsedInstruction!.IsEnd.Should().BeTrue();
        end.ParsedInstruction.Instructions.Should().BeEmpty();
    }

    [Fact]
    public void DotEndWithoutAFunctionIsAnError()
    {
        AssertError(".end\nreti\n", "*.end' without a '.func'*");
        AssertError(".func f:\nret\n.end\n.end\n", "*.end' without a '.func'*");
    }

    [Fact]
    public void APhysicalRegionKeepsItsOwnR7AndStackAndItsBytes()
    {
        //the acceptance rule of A2: a source written in physical registers only assembles
        //exactly as before - adding '.end' must not move a single byte
        var withEnd = Harness.Assemble(".func f:\nli r7, 5\npush r8\npop r8\nret\n.end\nreti\n");
        var withoutEnd = Harness.Assemble(".func f:\nli r7, 5\npush r8\npop r8\nret\nreti\n");

        withEnd.Should().Equal(withoutEnd);
    }

    [Fact]
    public void LabelReferencesDoNotMakeARegionNamed()
    {
        Compile("""
            .func f:
            j done
            call f
            dw done
            done: nop
            reti
            .end
            """);
    }

    [Theory]
    //the parameter list alone makes the region named
    [InlineData(".func mulsum(a, b)\n.end\n", "*mulsum*: virtual names are not assembled yet*")]
    //a name in a memory position, after 'ret' or in a register position makes it named too
    [InlineData(".func f:\nstw [buf+0], r1\nldw r2, [buf+0]\n.end\n", "*region of 'f': virtual names*")]
    [InlineData(".func f:\nmov acc, r0\n.end\n", "*region of 'f': virtual names*")]
    [InlineData(".func f:\nret acc\n.end\n", "*region of 'f': virtual names*")]
    public void ANamedRegionIsRecognizedAndGated(string source, string messagePattern)
    {
        AssertError(source, messagePattern);
    }

    [Theory]
    [InlineData(".func f(a)\nli r7, 3\n.end\n", "*r7 is not allowed in a named region*")]
    [InlineData(".func f(a)\nmov r1, r14\n.end\n", "*r14 is not allowed in a named region*")]
    [InlineData(".func f(a)\nadd r15, 4\n.end\n", "*r15 is not allowed in a named region*")]
    [InlineData(".func f(a)\npush r8\n.end\n", "*'push' is not allowed in a named region*")]
    [InlineData(".func f(a)\npop r8\n.end\n", "*'pop' is not allowed in a named region*")]
    public void TheMachineRegistersAndTheStackBelongToANamedRegion(string source, string messagePattern)
    {
        AssertError(source, messagePattern);
    }

    [Fact]
    public void AFrameSlotBaseMustNotBeAnEquConstant()
    {
        //doc 7.2: a constant hiding behind a memory position assembles as a number and silently
        //stops being a variable
        AssertError(".equ UART0, 0\n.func f(a)\nstw [UART0+1], r0\n.end\n",
            "*'UART0' is an .equ constant, not a frame slot*");
    }

    [Fact]
    public void ANameMayNotAlsoBeALabelOfTheSameRegion()
    {
        AssertError(".func f(a)\nslot: nop\nldw r1, [slot+0]\n.end\n",
            "*name 'slot' is also a label of the region of 'f'*");
    }

    [Theory]
    [InlineData(".func (a)\n", "*'.func' expects a name*")]
    [InlineData(".func f(r3)\n", "*'r3' is a register, it cannot be a parameter*")]
    [InlineData(".func f(add)\n", "*parameter 'add' is a mnemonic*")]
    [InlineData(".func f(f)\n", "*parameter 'f' repeats the function name*")]
    [InlineData(".func f(a, A)\n", "*parameter 'A' is listed twice*")]
    [InlineData(".func f(a, b\n", "*parameter list of 'f' has no ')'*")]
    [InlineData(".func f(a) nop\n", "*unexpected text after the parameter list*")]
    [InlineData(".func f: (a)\n", "*parameters follow 'f' directly, without ':'*")]
    [InlineData(".func f(p1, p2, p3, p4, p5, p6, p7)\n", "*has 7 parameters, r0..r5 carry at most 6*")]
    [InlineData(".equ MAX, 5\n.func f(MAX)\n", "*parameter 'MAX' is already an .equ constant*")]
    public void MalformedParameterListsAreNamedWithTheirMistake(string source, string messagePattern)
    {
        AssertError(source, messagePattern);
    }

    [Fact]
    public void ARegionEndsWhereTheNextFuncBegins()
    {
        //no '.end' at all: the second '.func' closes the first region, and the error names it
        AssertError(".func f:\nmov acc, r0\n.func g:\nnop\n",
            "*region of 'f': virtual names are not assembled yet*");
    }

    [Fact]
    public void AnEmptyParameterListLeavesAPhysicalRegion()
    {
        Compile(".func f()\nnop\nret\n.end\n");
    }

    [Fact]
    public void RetValueAssemblesAndExecutesInAPhysicalRegion()
    {
        //'ret r3' is mov r0, r3 plus the usual return, so the caller reads the value in r0
        var cpu = Harness.Program("""
            .func main:
            call work
            reti
            .end
            .func work:
            movi r3, 42
            ret r3
            .end
            """);

        var last = cpu.Run();

        last.Reason.Should().Be(StopReason.Reti);
        cpu[0].Should().Be(42);
    }
}
