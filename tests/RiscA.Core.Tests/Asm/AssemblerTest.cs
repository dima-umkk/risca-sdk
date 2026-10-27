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

    private Assembler Compile(string source)
    {
        var asm = new Assembler(0);
        asm.Compile(Write(source));
        return asm;
    }

    private static string Nops(int n) => string.Concat(Enumerable.Repeat("nop\n", n));

    private static SrcLine LabelLine(Assembler asm, string label)
        => asm.Src.Single(l => l.ParsedInstruction?.Label == label);

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

    [Fact]
    public void A_label_may_be_defined_only_once()
    {
        //the second definition used to silently replace the first, so every reference pointed
        //at the last one and the mistake stayed invisible
        var asm = new Assembler(0);
        var file = Write("start: nop\nnop\nstart: nop\nreti\n");

        Action act = () => asm.Compile(file);

        act.Should().Throw<Exception>().WithMessage("*label 'start' is already defined at*");
    }

    //Relaxation of the far label references (doc/assembler-architecture.md, 6.4). The expansions
    //change the program size, the addresses move, so the assembler iterates until the layout is stable.

    [Fact]
    public void Far_branch_becomes_an_inverted_branch_plus_jr_with_a_hidden_fallthrough_label()
    {
        //300 NOPs put 'far' past the +-128 instructions of the branch
        var asm = Compile("beqz r1, far\n" + Nops(300) + "far: reti\n");

        var first = asm.Src[0].ParsedInstruction!.Instructions;
        first.Should().HaveCount(2);
        first[0].OpCode.Should().Be(OpCode.BRANCH);
        first[0].Func1.Should().Be(1);//beqz inverted to bnez: the jr runs only when the branch is taken
        first[0].Rd.Should().Be(1);
        //the inverted branch skips over the inserted jr to the hidden label behind the line:
        //branch offsets count from the branch itself, so the jr right behind it makes it +2
        first[0].Imm8s.Should().Be(2);

        //the zero instruction line right behind carries the hidden @over label the branch skips over
        var over = asm.Src[1];
        over.ParsedInstruction!.Instructions.Should().BeEmpty();
        over.ParsedInstruction.Label.Should().StartWith("@over");
        first[0].CalcRef(0).Should().Be(over.Address);

        //the inserted jr covers the original distance on its own (JR steps in instructions)
        first[1].Func2.Should().Be((int)CallJmpRetFunc.JR);
        first[1].CalcRef(2).Should().Be(LabelLine(asm, "far").Address);
    }

    [Fact]
    public void Near_branch_is_left_as_one_instruction()
    {
        var beqz = CompileFirst("beqz r1, near\nnop\nnop\nnear: reti\n");

        beqz.OpCode.Should().Be(OpCode.BRANCH);
        beqz.Func1.Should().Be(0);
        beqz.Imm8s.Should().Be(3);
    }

    [Fact]
    public void Far_backward_branch_is_relaxed_the_same_way()
    {
        var asm = Compile("loop: nop\n" + Nops(300) + "bnez r2, loop\nreti\n");

        //the relaxed line is the one that ends up with the inserted jr pointing back at 'loop'
        var line = asm.Src.Single(l => l.ParsedInstruction?.Fixups.Exists(f => f.Kind == FixupKind.Jr) == true);
        var ins = line.ParsedInstruction!.Instructions;

        ins.Should().HaveCount(2);
        ins[0].OpCode.Should().Be(OpCode.BRANCH);
        ins[0].Func1.Should().Be(0);//bnez inverted to beqz
        ins[0].Imm8s.Should().Be(2);//its hidden fallthrough sits right behind the inserted jr
        //the jr stands right behind the branch inside the expanded line and jumps back to 'loop'
        ins[1].CalcRef(line.Address + 2).Should().Be(LabelLine(asm, "loop").Address);
    }

    [Fact]
    public void Far_call_becomes_ldi_r7_call_r7_with_an_inline_mini_pool()
    {
        //2100 NOPs put 'far' past the +-1024 words of CALL; the pool slot at the end of the region
        //is out of the +-256 instructions of LDI as well, so the address travels in a dw pair
        //right on the spot (doc 6.1): ldi +2, jr +3 over the data, dw, then the real CALL r7
        var asm = Compile("call far\n" + Nops(2100) + "far: ret\n");

        var ins = asm.Src[0].ParsedInstruction!.Instructions;
        ins.Should().HaveCount(5);
        ins[0].OpCode.Should().Be(OpCode.LDI);
        ins[0].Rd.Should().Be(7);//r7 is the scratch register of the ABI
        ins[0].CalcRef(0).Should().Be(4);//the dw pair of the inline pool
        ins[1].Func2.Should().Be((int)CallJmpRetFunc.JR);
        ins[1].CalcRef(2).Should().Be(8);//jr lands behind the dw pair, on the CALL itself

        var address = (uint)ins[2].Raw | (uint)ins[3].Raw << 16;
        address.Should().Be((uint)LabelLine(asm, "far").Address);

        ins[4].OpCode.Should().Be(OpCode.CALL_JMP_RET);
        ins[4].Func2.Should().Be((int)CallJmpRetFunc.CALL_REG);
        ins[4].Rd.Should().Be(7);
    }

    [Fact]
    public void Far_j_becomes_ldi_rN_jmp_rN_and_keeps_the_requested_register()
    {
        //1100 NOPs put 'far' past the +-1024 instructions of JR, r3 comes from 'j far, r3'
        var asm = Compile("j far, r3\n" + Nops(1100) + "far: ret\n");

        var ins = asm.Src[0].ParsedInstruction!.Instructions;
        ins.Should().HaveCount(5);
        ins[0].OpCode.Should().Be(OpCode.LDI);
        ins[0].Rd.Should().Be(3);
        ins[1].Func2.Should().Be((int)CallJmpRetFunc.JR);
        var address = (uint)ins[2].Raw | (uint)ins[3].Raw << 16;
        address.Should().Be((uint)LabelLine(asm, "far").Address);
        ins[4].Func2.Should().Be((int)CallJmpRetFunc.RET);//RET with Rd != 14 is JMP Rd
        ins[4].Rd.Should().Be(3);
    }

    [Fact]
    public void La_reads_the_address_of_a_nearby_label_from_the_pool_of_its_region()
    {
        var asm = Compile("la r0, msg\nnop\nmsg: dw 7\n");

        //one pool slot, placed at the end of the region, holds the absolute address of 'msg'
        var pool = asm.Src.Single(l => l.ParsedInstruction?.IsPool == true);
        pool.ParsedInstruction!.Instructions[0].Raw.Should().Be((ushort)LabelLine(asm, "msg").Address);
        pool.ParsedInstruction.Instructions[1].Raw.Should().Be(0);

        //the la line stays a single LDI: the slot is close, its address is reachable with imm9
        var la = asm.Src[0].ParsedInstruction!;
        la.Instructions.Should().HaveCount(1);
        la.Instructions[0].CalcRef(0).Should().Be(pool.Address);
    }

    [Fact]
    public void Each_function_region_gets_its_own_pool_slot_for_a_label()
    {
        var asm = Compile(".func f1:\nla r0, msg\nret\n.func f2:\nla r0, msg\nret\nmsg: dw 0\n");

        asm.Src.Count(l => l.ParsedInstruction?.IsPool == true).Should().Be(2);
        //the pool of f1 sits inside the f1 region, right before the next .func line
        var f1 = LabelLine(asm, "f1");
        var f2 = LabelLine(asm, "f2");
        asm.Src.Count(l => l.ParsedInstruction?.IsPool == true
            && l.Address > f1.Address && l.Address < f2.Address).Should().Be(1);
    }

    [Fact]
    public void Hand_written_ldi_to_a_far_label_is_an_error_not_a_relaxation()
    {
        //'ldi r0, [far]' reads the contents at the label: there is no data to move closer,
        //so the assembler says so instead of growing the code (doc 6.1)
        Action act = () => Compile("ldi r0, [far]\n" + Nops(300) + "far: reti\n");

        act.Should().Throw<Exception>().WithMessage("*Reference address to far*");
    }

    [Fact]
    public void Dw_with_a_label_holds_the_absolute_address_of_the_label()
    {
        //every absolute address of the image lives in a dw entry: this set of entries later
        //doubles as the relocation table of 'load the program anywhere' (doc 7)
        var asm = Compile("dw target\ntarget: nop\nreti\n");

        var dw = asm.Src[0].ParsedInstruction!.Instructions;
        dw[0].Raw.Should().Be((ushort)LabelLine(asm, "target").Address);
        dw[1].Raw.Should().Be(0);
    }
}
