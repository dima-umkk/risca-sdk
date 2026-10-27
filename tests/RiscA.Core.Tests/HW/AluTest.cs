using RiscA.Core.ISA;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

//Opcode 0, 1 and 2: the ALU and the constant builders of doc/RiscA.MD lines 31-76.
public class AluTest
{
    //one instruction, the registers preset, the value it produced in Rd
    private static uint Result(Instruction inst, int target, params (int reg, int value)[] preset)
    {
        var cpu = Harness.Cpu(inst).With(preset);

        cpu.Step().Reason.Should().Be(StopReason.Running);

        return cpu[target];
    }

    [Theory]
    [InlineData(ALUFunc.MOV, 3u, 5u, 5u)]
    [InlineData(ALUFunc.ADD, 3u, 5u, 8u)]
    [InlineData(ALUFunc.ADD, 0xFFFF_FFFEu, 3u, 1u)]          //wraps around, there are no flags
    [InlineData(ALUFunc.SUB, 3u, 5u, 0xFFFF_FFFEu)]
    [InlineData(ALUFunc.AND, 0xF0F0u, 0x0FF0u, 0x00F0u)]
    [InlineData(ALUFunc.OR, 0xF0F0u, 0x000Fu, 0xF0FFu)]
    [InlineData(ALUFunc.XOR, 0xF0F0u, 0xF0AAu, 0x005Au)]
    [InlineData(ALUFunc.NOT, 0u, 0xFFFF_FFF0u, 0x0000_000Fu)]
    [InlineData(ALUFunc.MUL, 3u, 5u, 15u)]
    [InlineData(ALUFunc.MUL, 0x0001_0000u, 0x0001_0000u, 0u)] //only the low 32 bits of the product
    [InlineData(ALUFunc.SLT, 0xFFFF_FFFFu, 1u, 1u)]           //signed: -1 is less than 1
    [InlineData(ALUFunc.SLT, 1u, 0xFFFF_FFFFu, 0u)]
    [InlineData(ALUFunc.SLT, 0u, 0u, 0u)]
    [InlineData(ALUFunc.SLTU, 0xFFFF_FFFFu, 1u, 0u)]          //unsigned: 1 is less than 0xFFFFFFFF
    [InlineData(ALUFunc.SLTU, 1u, 0xFFFF_FFFFu, 1u)]
    public void Reg_reg_operations(ALUFunc func, uint left, uint right, uint expected)
    {
        //Rd is both the destination and the left operand, except for NOT, which only reads Rs
        Result(Code.Alu(func, 1, 2), 1, (1, unchecked((int)left)), (2, unchecked((int)right)))
            .Should().Be(expected);
    }

    [Fact]
    public void Not_ignores_the_previous_value_of_the_destination()
    {
        //doc/RiscA.MD line 43: NOT Rd, Rs computes Rd = ~Rs, the old Rd plays no part
        Result(Code.Not(1, 2), 1, (1, 0x1234_5678), (2, unchecked((int)0xFFFF_FFF0))).Should().Be(0x0000_000F);
    }

    [Theory]
    [InlineData(0u, 0u, 0u)]
    [InlineData(0x0000_0001u, 0u, 0x0000_0001u)]
    [InlineData(0x0000_0001u, 15u, 0x0000_8000u)]
    [InlineData(0xFFFF_FFFFu, 15u, 0xFFFF_8000u)]
    [InlineData(0x1234_5678u, 4u, 0x2345_6780u)]
    public void Shift_left_by_the_immediate(uint value, int amount, uint expected)
        => Result(Code.Shl(1, amount), 1, (1, unchecked((int)value))).Should().Be(expected);

    [Theory]
    [InlineData(0x0000_8000u, 15u, 0x0000_0001u)]
    [InlineData(0xFFFF_FFFFu, 15u, 0x0001_FFFFu)]             //logical, not arithmetic: no sign extension
    [InlineData(0x8000_0000u, 15u, 0x0001_0000u)]             //the sign bit moves down, it is not copied
    [InlineData(0x8765_4321u, 12u, 0x0008_7654u)]
    public void Shift_right_logical_by_the_immediate(uint value, int amount, uint expected)
        => Result(Code.Shr(1, (int)amount), 1, (1, unchecked((int)value))).Should().Be(expected);

    //the assembler rejects a shift over 15, a hand made image behaves like hardware wiring only Imm(4) to the shifter (doc/RiscA.MD lines 59-60)
    [Theory]
    [InlineData(16, 0)]
    [InlineData(17, 1)]
    [InlineData(31, 15)]
    public void The_shift_amount_of_a_raw_encoding_is_masked_to_four_bits(int imm7, int effective)
    {
        var inst = Code.AluImmRaw(ALUImmFunc.SHL, 1, imm7);
        inst.Imm7.Should().Be(imm7);

        Result(inst, 1, (1, 1)).Should().Be((uint)(1 << effective));
    }

    [Theory]
    [InlineData(3u, 0u, 3u)]
    [InlineData(3u, 127u, 130u)]
    [InlineData(0xFFFF_FFFEu, 3u, 1u)]
    public void Add_immediate(uint value, int imm, uint expected)
        => Result(Code.Addi(1, imm), 1, (1, unchecked((int)value))).Should().Be(expected);

    [Theory]
    [InlineData(3u, 5u, 0xFFFF_FFFEu)]
    [InlineData(130u, 127u, 3u)]
    [InlineData(0u, 1u, 0xFFFF_FFFFu)]
    public void Subtract_immediate(uint value, uint imm, uint expected)
        => Result(Code.Subi(1, (int)imm), 1, (1, unchecked((int)value))).Should().Be(expected);

    [Fact]
    public void Movi_writes_the_unsigned_eight_bit_immediate()
    {
        Result(Code.Movi(3, 0xFF), 3).Should().Be(0xFF);
        Result(Code.Movi(3, 0), 3, (3, 0x1234_5678)).Should().Be(0);
    }

    [Fact]
    public void Movl_shifts_the_register_left_and_inserts_the_immediate()
    {
        //doc/RiscA.MD line 76: Rd = (Rd << 8) | Imm(8)
        Result(Code.Movl(3, 0x34), 3, (3, 0x12)).Should().Be(0x1234);
    }

    [Fact]
    public void A_constant_of_any_size_is_built_by_a_sequence_of_movi_and_movl()
    {
        var cpu = Harness.Cpu(Code.Movi(3, 0x12), Code.Movl(3, 0x34), Code.Movl(3, 0x56), Code.Movl(3, 0x78));

        cpu.Run(4);

        cpu[3].Should().Be(0x1234_5678);
        cpu.InstructionsRetired.Should().Be(4);
    }

    [Fact]
    public void Nop_is_mov_r0_r0_and_changes_nothing()
    {
        var cpu = Harness.Cpu(Code.Nop).With((0, 42), (15, 0x1000));

        var step = cpu.Step();

        step.Reason.Should().Be(StopReason.Running);
        step.Instruction.Raw.Should().Be(0);
        cpu[0].Should().Be(42);
        cpu[15].Should().Be(0x1000);
        cpu.PC.Should().Be(Harness.Origin + 2);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(15)]
    public void Func4_values_the_spec_leaves_unused_are_illegal(int func4)
    {
        var cpu = Harness.Cpu(Code.AluFunc4(func4, 1, 2));

        var step = cpu.Step();

        step.Reason.Should().Be(StopReason.IllegalInstruction);
        step.Message.Should().Be($"ALU func4 {func4} is not used by the spec");
        cpu.Halted.Should().BeTrue();
        //the PC stays on the illegal instruction and nothing is retired
        cpu.PC.Should().Be(Harness.Origin);
        cpu.InstructionsRetired.Should().Be(0);
    }
}
