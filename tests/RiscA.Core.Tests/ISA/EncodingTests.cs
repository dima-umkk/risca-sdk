using RiscA.Core.ISA;
using FluentAssertions;
using System;

namespace RiscA.Core.Tests.ISA;

public class EncodingTests
{
    // ALU REG REG: unused(1) | func4(4) | Rs(4) | Rd(4) | opcode(3)
    [Theory]
    [InlineData((int)ALUFunc.SLT,  1, 2, (ushort)0x4108)]
    [InlineData((int)ALUFunc.SLTU, 3, 4, (ushort)0x4A18)]
    [InlineData((int)ALUFunc.MOV,  1, 2, (ushort)0x0108)]
    [InlineData((int)ALUFunc.MUL,  13, 14, (ushort)0x3F68)]
    public void ALU_reg_reg_encodes_func4_Rs_Rd_and_opcode(int func4, int rd, int rs, ushort expected)
    {
        var inst = new Instruction(0)
            .withOpCode(OpCode.ALU_REG_REG)
            .withFunc4(func4)
            .withRd(rd)
            .withRs(rs);

        inst.Raw.Should().Be(expected);
        inst.OpCode.Should().Be(OpCode.ALU_REG_REG);
        inst.Func4.Should().Be(func4);
        inst.Rd.Should().Be(rd);
        inst.Rs.Should().Be(rs);
    }

    [Theory]
    [InlineData((int)ALUFunc.SLT)]
    [InlineData((int)ALUFunc.SLTU)]
    public void ALU_func4_never_touches_bit_15(int func4)
    {
        var inst = new Instruction(0)
            .withOpCode(OpCode.ALU_REG_REG)
            .withFunc4(func4)
            .withRd(15)
            .withRs(15);

        (inst.Raw & 0x8000).Should().Be(0);
        new Instruction(inst.Raw).Func4.Should().Be(func4);
    }

    // BRANCH: Imm(8) | func1(1) | Rd(4) | opcode(3)
    [Theory]
    [InlineData((int)BranchFunc.BEQZ, 0, 4, (ushort)0x0404)]
    [InlineData((int)BranchFunc.BNEZ, 1, -2, (ushort)0xFE8C)]
    [InlineData((int)BranchFunc.BEQZ, 15, 127, (ushort)0x7F7C)]
    [InlineData((int)BranchFunc.BNEZ, 0, -128, (ushort)0x8084)]
    public void BRANCH_encodes_imm8_func1_Rd_and_opcode(int func1, int rd, int imm, ushort expected)
    {
        var inst = new Instruction(0)
            .withOpCode(OpCode.BRANCH)
            .withFunc1(func1)
            .withRd(rd)
            .withImm8(imm);

        inst.Raw.Should().Be(expected);
        inst.OpCode.Should().Be(OpCode.BRANCH);
        inst.Func1.Should().Be(func1);
        inst.Rd.Should().Be(rd);
        inst.Imm8s.Should().Be(imm);
    }

    [Theory]
    [InlineData(0, 4, 0x208)]
    [InlineData(0, -2, 0x1FC)]
    [InlineData(15, -128, 0x100)]
    [InlineData(3, 127, 0x2FE)]
    public void BRANCH_target_is_address_plus_imm8_times_two(int rd, int imm, int expected)
    {
        var inst = new Instruction(0)
            .withOpCode(OpCode.BRANCH)
            .withFunc1((int)BranchFunc.BEQZ)
            .withRd(rd)
            .withImm8(imm);

        inst.CalcRef(0x200).Should().Be(expected);
    }

    [Theory]
    [InlineData(-128)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(127)]
    public void BRANCH_accepts_signed_imm8_range(int imm)
    {
        var inst = new Instruction(0).withOpCode(OpCode.BRANCH);

        var act = () => inst.CheckImmLimits(imm);

        act.Should().NotThrow();
        inst.SetImm(imm).Imm8s.Should().Be(imm);
    }

    [Theory]
    [InlineData(128)]
    [InlineData(-129)]
    [InlineData(255)]
    public void BRANCH_rejects_values_outside_signed_imm8_range(int imm)
    {
        var inst = new Instruction(0).withOpCode(OpCode.BRANCH);

        var act = () => inst.CheckImmLimits(imm);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*-128 .. 127*");
    }
}
