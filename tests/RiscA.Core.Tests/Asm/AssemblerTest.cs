using RiscA.Core.Asm;
using RiscA.Core.ISA;

namespace RiscA.Core.Tests.Asm;

//Label resolution for the jump instructions. The offsets are counted differently for CALL and JR:
//CALL steps in 4 byte words (doc/RiscA.MD line 124), JR steps in instructions (line 127).
public class AssemblerTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "risca-asm-" + Guid.NewGuid().ToString("N"));

    public AssemblerTest()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Write(string source, string name = "prog.rasm")
    {
        var file = Path.Combine(_dir, name);
        File.WriteAllText(file, source);
        return file;
    }

    private Instruction CompileFirst(string source)
    {
        var asm = new Assembler(0);
        asm.Compile(Write(source));
        return asm.Src[0].ParsedInstruction!.Instructions[0];
    }

    [Fact]
    public void JR_label_offset_is_counted_in_instructions_not_in_words()
    {
        //jr at 0x00, the label is one instruction (2 bytes) away
        var jr = CompileFirst("jr target\ntarget: nop\nreti\n");

        jr.OpCode.Should().Be(OpCode.CALL_JMP_RET);
        jr.Func2.Should().Be((int)CallJmpRetFunc.JR);
        jr.ImmCallJr.Should().Be(1);
        jr.CalcRef(0).Should().Be(2);
    }

    [Fact]
    public void JR_backwards_label_offset_is_negative()
    {
        //jr at 0x06, the label is at 0x00
        var asm = new Assembler(0);
        asm.Compile(Write("loop: nop\nnop\nnop\njr loop\n"));

        var jr = asm.Src[3].ParsedInstruction!.Instructions[0];

        jr.ImmCallJr.Should().Be(-3);
        jr.CalcRef(6).Should().Be(0);
    }

    [Fact]
    public void CALL_label_offset_is_counted_in_words()
    {
        //call at 0x00, the label is at 0x08 = 2 words
        var call = CompileFirst("call target\nnop\nnop\nnop\ntarget: ret\n");

        call.OpCode.Should().Be(OpCode.CALL_JMP_RET);
        call.Func2.Should().Be((int)CallJmpRetFunc.CALL_IMM);
        call.ImmCallJr.Should().Be(2);
        call.CalcRef(0).Should().Be(8);
    }

    [Fact]
    public void JR_may_jump_to_a_label_that_is_not_word_aligned()
    {
        var jr = CompileFirst("jr target\nnop\nnop\ntarget: reti\n");

        jr.ImmCallJr.Should().Be(3);
        jr.CalcRef(0).Should().Be(6);
    }

    [Fact]
    public void CALL_must_not_jump_to_a_label_that_is_not_word_aligned()
    {
        var asm = new Assembler(0);
        var file = Write("call target\nnop\nnop\ntarget: ret\n");

        Action act = () => asm.Compile(file);

        act.Should().Throw<Exception>().WithMessage("*not word aligned*");
    }
}
