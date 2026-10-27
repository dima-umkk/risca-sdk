using RiscA.Core.ISA;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

//Builders for single instructions, so that a test reads like the assembly it describes.
internal static class Code
{
    public static Instruction Raw(ushort raw) => new(raw);

    public static Instruction Nop => new(0);

    public static Instruction Alu(ALUFunc func, int rd, int rs)
        => new Instruction(0).withOpCode(OpCode.ALU_REG_REG).withFunc4((int)func).withRd(rd).withRs(rs);

    public static Instruction Mov(int rd, int rs) => Alu(ALUFunc.MOV, rd, rs);
    public static Instruction Add(int rd, int rs) => Alu(ALUFunc.ADD, rd, rs);
    public static Instruction Sub(int rd, int rs) => Alu(ALUFunc.SUB, rd, rs);
    public static Instruction And(int rd, int rs) => Alu(ALUFunc.AND, rd, rs);
    public static Instruction Or(int rd, int rs) => Alu(ALUFunc.OR, rd, rs);
    public static Instruction Xor(int rd, int rs) => Alu(ALUFunc.XOR, rd, rs);
    public static Instruction Not(int rd, int rs) => Alu(ALUFunc.NOT, rd, rs);
    public static Instruction Mul(int rd, int rs) => Alu(ALUFunc.MUL, rd, rs);
    public static Instruction Slt(int rd, int rs) => Alu(ALUFunc.SLT, rd, rs);
    public static Instruction Sltu(int rd, int rs) => Alu(ALUFunc.SLTU, rd, rs);

    //an ALU reg/reg instruction with an arbitrary func4, including the values the spec leaves unused
    public static Instruction AluFunc4(int func4, int rd, int rs)
        => new Instruction(0).withOpCode(OpCode.ALU_REG_REG).withFunc4(func4).withRd(rd).withRs(rs);

    public static Instruction AluImm(ALUImmFunc func, int rd, int imm)
        => new Instruction(0).withOpCode(OpCode.ALU_REG_IMM).withFunc2((int)func).withRd(rd).SetImm(imm);

    public static Instruction Shl(int rd, int imm) => AluImm(ALUImmFunc.SHL, rd, imm);
    public static Instruction Shr(int rd, int imm) => AluImm(ALUImmFunc.SHR, rd, imm);
    public static Instruction Addi(int rd, int imm) => AluImm(ALUImmFunc.ADD, rd, imm);
    public static Instruction Subi(int rd, int imm) => AluImm(ALUImmFunc.SUB, rd, imm);

    //an ALU imm instruction with Imm(7) written straight into the encoding, used to test the shift mask
    public static Instruction AluImmRaw(ALUImmFunc func, int rd, int imm7)
        => new Instruction(0).withOpCode(OpCode.ALU_REG_IMM).withFunc2((int)func).withRd(rd).withImm7(imm7);

    public static Instruction Movi(int rd, int imm)
        => new Instruction(0).withOpCode(OpCode.REG_IMM).withFunc1((int)RegImmFunc.MOVI).withRd(rd).withImm8(imm);

    public static Instruction Movl(int rd, int imm)
        => new Instruction(0).withOpCode(OpCode.REG_IMM).withFunc1((int)RegImmFunc.MOVL).withRd(rd).withImm8(imm);

    public static Instruction Ldw(int rd, int rs, int offset) => LdSt(false, true, rd, rs, offset);
    public static Instruction Ldb(int rd, int rs, int offset) => LdSt(false, false, rd, rs, offset);
    public static Instruction Stw(int rd, int rs, int offset) => LdSt(true, true, rd, rs, offset);
    public static Instruction Stb(int rd, int rs, int offset) => LdSt(true, false, rd, rs, offset);

    private static Instruction LdSt(bool store, bool word, int rd, int rs, int offset)
        => new Instruction(0)
            .withOpCode(OpCode.ST_LD)
            .withFunc21(store ? (int)LdStFunc.ST : (int)LdStFunc.LD)
            .withFunc22(word ? (int)LdStBWFunc.W : (int)LdStBWFunc.B)
            .withRd(rd).withRs(rs).withImm3(offset);

    public static Instruction Beqz(int rd, int offset) => Branch(BranchFunc.BEQZ, rd, offset);
    public static Instruction Bnez(int rd, int offset) => Branch(BranchFunc.BNEZ, rd, offset);

    private static Instruction Branch(BranchFunc func, int rd, int offset)
        => new Instruction(0).withOpCode(OpCode.BRANCH).withFunc1((int)func).withRd(rd).withImm8(offset);

    public static Instruction Ldi(int rd, int offset)
        => new Instruction(0).withOpCode(OpCode.LDI).withRd(rd).withImm9(offset);

    //offset of CALL is in words, the offset of JR is in instructions
    public static Instruction Call(int words)
        => new Instruction(0).withOpCode(OpCode.CALL_JMP_RET).withFunc2((int)CallJmpRetFunc.CALL_IMM).SetImm(words);
    public static Instruction CallReg(int rd)
        => new Instruction(0).withOpCode(OpCode.CALL_JMP_RET).withFunc2((int)CallJmpRetFunc.CALL_REG).withRd(rd);
    public static Instruction Ret(int rd = Registers.LinkRegister)
        => new Instruction(0).withOpCode(OpCode.CALL_JMP_RET).withFunc2((int)CallJmpRetFunc.RET).withRd(rd);
    public static Instruction Jr(int instructions)
        => new Instruction(0).withOpCode(OpCode.CALL_JMP_RET).withFunc2((int)CallJmpRetFunc.JR).SetImm(instructions);

    public static Instruction Int(int rd)
        => new Instruction(0).withOpCode(OpCode.INT_RETI).withFunc2(0).withRd(rd);
    public static Instruction Reti()
        => new Instruction(0).withOpCode(OpCode.INT_RETI).withFunc2(1);
    public static Instruction GetEpc(int rd)
        => new Instruction(0).withOpCode(OpCode.INT_RETI).withFunc2(2).withRd(rd);
    public static Instruction SetEpc(int rd)
        => new Instruction(0).withOpCode(OpCode.INT_RETI).withFunc2(3).withRd(rd);
}
