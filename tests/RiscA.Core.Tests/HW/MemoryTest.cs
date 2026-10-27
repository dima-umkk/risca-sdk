using RiscA.Core.ISA;
using System;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

//Opcode 3 and 5: LDB/LDW/STB/STW and LDI in the one flat address bus (doc/RiscA.MD lines 78-96, 128-136).
public class MemoryTest
{
    private const uint Ram = Harness.Origin;

    [Fact]
    public void A_word_goes_to_ram_and_comes_back()
    {
        var cpu = Harness.Cpu(Code.Stw(2, 3, 3), Code.Ldw(4, 3, 3)).With((2, 0x1234_5678), (3, (int)Ram + 0x20));

        cpu.Step();
        cpu.Step().Reason.Should().Be(StopReason.Running);

        cpu.Regs[4].Should().Be(0x1234_5678);
        //Imm(3) is a word offset for word access: slot 3 is byte 0xC, so the word sits at Rs + 0x2C
        cpu.Bus.ReadWord(Ram + 0x2C).Should().Be(0x1234_5678);
    }

    [Fact]
    public void Word_access_scales_the_offset_by_four()
    {
        //doc/RiscA.MD line 85: for word access Imm(3) << 2 is used, for byte access the raw Imm(3)
        var cpu = Harness.Cpu(Code.Stw(1, 2, 1)).With((1, 0xAB), (2, (int)Ram + 0x40));

        cpu.Step();

        cpu.Bus.ReadWord(Ram + 0x44).Should().Be(0xAB);
        cpu.Bus.ReadWord(Ram + 0x40).Should().Be(0);
    }

    [Fact]
    public void Byte_store_touches_one_byte_lane_of_ram()
    {
        var cpu = Harness.Cpu(Code.Stb(1, 2, 1)).With((1, 0x0000_0077), (2, (int)Ram + 0x10));

        cpu.Step();

        //the byte address is Rs + Imm(3) without scaling, the other lanes of the word are untouched
        cpu.Bus.ReadWord(Ram + 0x10).Should().Be(0x0000_7700);
    }

    [Fact]
    public void Byte_store_writes_the_low_byte_of_the_register()
    {
        var cpu = Harness.Cpu(Code.Stb(1, 2, 0)).With((1, unchecked((int)0x1122_3344)), (2, (int)Ram + 0x10));

        cpu.Step();

        cpu.Bus.ReadByte(Ram + 0x10).Should().Be(0x44);
    }

    [Fact]
    public void Byte_load_is_zero_extended()
    {
        var cpu = Harness.Cpu(Code.Ldb(1, 2, 3)).With((2, (int)Ram + 0x10));
        cpu.Bus.WriteByte(Ram + 0x13, 0xFF);

        cpu.Step();

        //LDB fills the high bits with zeros, it is not sign extended (doc/README.md, opcode 3)
        cpu.Regs[1].Should().Be(0xFF);
    }

    [Fact]
    public void The_program_patches_its_own_code_and_the_new_instruction_runs()
    {
        //self modifying code works because the fetch path and the data path see the same map (doc/emulator-architecture.md, §3)
        var cpu = Harness.Cpu(
            Code.Movi(2, 7),
            Code.Stb(2, 15, 1),      //patch the high byte of the instruction the register points at
            Code.Jr(1),              //step onto the patched instruction
            Code.Movi(0, 1),         //0x0102, it becomes 0x0702 = movi r0, 7
            Code.Ret());
        cpu.Regs[15] = Harness.Origin + 6;

        cpu.Step();                 //movi r2, 7
        cpu.Step();                 //stb [r15+1], r2

        cpu.Bus.ReadHalfword(Harness.Origin + 6).Should().Be(0x0702);

        cpu.Step();                 //jr to the patched halfword
        cpu.Step().Reason.Should().Be(StopReason.Running);

        cpu.Regs[0].Should().Be(7); //the patched immediate took effect, it is not 1
    }

    [Fact]
    public void A_device_can_be_reached_from_the_data_path_with_a_byte_access()
    {
        var fake = new FakeDevice("fake", 1, [0xAABBCCDD]);
        var cpu = Harness.Cpu(Code.Ldb(0, 7, 2), Code.Stb(1, 7, 2));
        cpu.Bus.AddDevice("fake", 4, fake);
        cpu.With((1, 0x77), (7, 0x0000_0400));

        cpu.Step();
        cpu.Step().Reason.Should().Be(StopReason.Running);

        //byte offsets of a device register are the little endian byte lanes of the 32 bit register
        cpu.Regs[0].Should().Be(0xBB);
        fake.Slots[0].Should().Be(0xAA77CCDD);
    }

    [Fact]
    public void The_last_word_of_the_ram_is_still_reachable_and_the_one_past_it_faults()
    {
        var bus = Harness.NewBus(64);
        var cpu = new CPU(bus);
        //two identical loads, the second one is pointed past the end of the ram
        ProgramImage.Load(bus, Harness.Origin, [Code.Ldw(0, 1, 0), Code.Ldw(0, 1, 0), Code.Ret()]);
        cpu.Regs[1] = Harness.Origin + 60;

        cpu.Step().Reason.Should().Be(StopReason.Running);
        cpu.Regs[0].Should().Be(0);

        //the word would span 0x0001003E..0x00010041, one byte past the ram
        cpu.Regs[1] = Harness.Origin + 62;
        cpu.Step().Reason.Should().Be(StopReason.BusFault);
        cpu.PC.Should().Be(Harness.Origin + 2);
    }

    [Fact]
    public void The_program_pushes_and_pops_the_stack_of_the_abi()
    {
        //PUSH of doc/RiscA.MD lines 158-160: SUB R15, 4 and STW [R15+0], Rd; POP is LDW and ADD R15, 4
        var cpu = Harness.Cpu(
            Code.Subi(15, 4), Code.Stw(3, 15, 0), Code.Movi(3, 0), Code.Ldw(4, 15, 0), Code.Addi(15, 4), Code.Reti());
        cpu.Regs[15] = Ram + 0x800;
        cpu.Regs[3] = 0xDEADBEEF;

        cpu.Run().Reason.Should().Be(StopReason.Reti);

        cpu.Regs[4].Should().Be(0xDEADBEEF);
        cpu.Regs[15].Should().Be(Ram + 0x800);
        cpu.Bus.ReadWord(Ram + 0x7FC).Should().Be(0xDEADBEEF);
        cpu.InstructionsRetired.Should().Be(6);
    }

    [Fact]
    public void Unaligned_word_load_of_ram_reads_across_instruction_boundaries()
    {
        //the LDI pool keeps 32 bit constants 2 byte aligned: imm3 is 0 and the base register carries the odd address
        var cpu = Harness.Cpu(Code.Ldw(1, 2, 0)).With((2, (int)Ram + 3));
        cpu.Bus.WriteWord(Ram + 3, 0xAABBCCDD);

        cpu.Step();

        cpu.Regs[1].Should().Be(0xAABBCCDD);
    }

    [Fact]
    public void Ldi_reads_a_word_constant_from_the_same_address_space()
    {
        //LDI Rd, [Imm(9)]: PC relative in instructions (doc/RiscA.MD lines 128-136)
        var cpu = Harness.Cpu(Code.Ldi(0, 2), Code.Ret());
        cpu.Bus.WriteWord(Harness.Origin + 4, 0xDEADBEEF);

        cpu.Step().Reason.Should().Be(StopReason.Running);

        cpu.Regs[0].Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void Ldi_can_reach_backwards_as_well_as_forwards()
    {
        var cpu = Harness.Cpu(Code.Ret(), Code.Ret(), Code.Ret(), Code.Ldi(0, -3));
        cpu.PC = Harness.Origin + 6;                       //start on the LDI, the three instructions before it are data
        cpu.Bus.WriteWord(Harness.Origin, 0x0000_1234);  //an instruction of the program doubles as a constant

        var step = cpu.Step();
        step.Address.Should().Be(Harness.Origin + 6);
        step.Reason.Should().Be(StopReason.Running);

        cpu.Regs[0].Should().Be(0x0000_1234);
    }

    [Fact]
    public void A_device_register_is_reached_through_the_same_instructions_as_ram()
    {
        var uart = new FakeDevice("uart0", 4, [0, 1, 0, 1]);
        var cpu = Harness.Cpu(Code.Stw(1, 7, 2), Code.Ldw(0, 7, 3));
        cpu.Bus.AddDevice("uart0", 0, uart);
        cpu.With((1, 'A'), (7, 0));

        cpu.Step();
        cpu.Step();

        uart.Written.Should().HaveCount(1);
        uart.Written[0].Should().Be((8, (uint)'A'));        //slot 2 of block 0 is byte offset 8
        cpu.Regs[0].Should().Be(1);                         //slot 3, tx_ready
    }

    [Fact]
    public void A_program_that_reaches_past_the_last_register_of_a_device_stops_with_the_access()
    {
        var uart = new FakeDevice("uart0", 4, [0, 1, 0, 1]);
        var cpu = Harness.Cpu(Code.Ldw(0, 7, 7));           //slot 7, the device only has 4 registers
        cpu.Bus.AddDevice("uart0", 0, uart);
        cpu.With((7, 0));

        var step = cpu.Step();

        step.Reason.Should().Be(StopReason.BusFault);
        step.Message.Should().Be("device uart0 has 16 bytes of registers, the access at 0x0000001C reaches further");
    }
}
