using RiscA.Core.ISA;
using System;
using System.Linq;

namespace RiscA.Machine.Tests;

//Everything below follows doc/RiscA.MD line 4 "Byte order: Little-Endian" and matches the lane
//numbering of the upstream testbench (artifacts/rtl/tb/risca_tb.v lines 69-72), where the byte
//lane of the lowest byte address is dmem_wdata[7:0], the least significant byte of the bus.
public class EndiannessTest
{
    private static readonly uint Ram = Harness.Origin;

    [Fact]
    public void A_word_write_puts_the_low_byte_at_the_lowest_address()
    {
        var space = Harness.Space();

        space.WriteWord(Ram + 4, 0xAABB_CCDD);

        space.ReadByte(Ram + 4).Should().Be(0xDD);
        space.ReadByte(Ram + 5).Should().Be(0xCC);
        space.ReadByte(Ram + 6).Should().Be(0xBB);
        space.ReadByte(Ram + 7).Should().Be(0xAA);
        space.ReadWord(Ram + 4).Should().Be(0xAABB_CCDD);

        //a word of the 32 bit bus is two 16 bit instruction halves, the low half is at the low address
        space.ReadHalfword(Ram + 4).Should().Be(0xCCDD);
        space.ReadHalfword(Ram + 6).Should().Be(0xAABB);
    }

    [Fact]
    public void Bytes_written_one_by_one_form_the_word_the_cpu_reads()
    {
        var space = Harness.Space();
        byte[] bytes = [0xDD, 0xCC, 0xBB, 0xAA];

        for (int i = 0; i < bytes.Length; i++)
            space.WriteByte(Ram + (uint)i, bytes[i]);

        //the same order as the four byte strobes dmem_sel[0..3] of the rtl
        space.ReadWord(Ram).Should().Be(0xAABB_CCDD);
    }

    [Fact]
    public void The_image_holds_the_low_byte_of_an_instruction_first()
    {
        var space = Harness.Space();
        var code = new Instruction[] { Code.Movi(0, 0x2A) };

        ProgramImage.Load(space, Ram, code);

        //movi r0, 0x2A is 0x2A02, little-endian in the load image like `rasm -o prog.bin` writes it
        space.ReadByte(Ram).Should().Be(0x02);
        space.ReadByte(Ram + 1).Should().Be(0x2A);

        var cpu = new Machine(space);
        cpu.Step().Instruction.Raw.Should().Be(0x2A02);
    }

    [Fact]
    public void A_dw_constant_round_trips_through_the_real_assembler_into_one_little_endian_word()
    {
        var source = """
            	ldi r0, [answer]
            	reti
            answer:
            	dw 0x0BADF00D
            """;
        var image = Harness.Assemble(source);

        //dw emits the low halfword first, so the bytes of the constant are 0D F0 AD 0B
        image.TakeLast(4).Select(b => (int)b).Should().Equal(0x0D, 0xF0, 0xAD, 0x0B);

        var space = Harness.Space();
        var cpu = new Machine(space);
        ProgramImage.Load(space, cpu.PC, image);

        cpu.Run().Reason.Should().Be(StopReason.Reti);
        cpu.Regs[0].Should().Be(0x0BAD_F00D);
    }

    [Fact]
    public void A_db_string_keeps_its_byte_order_in_little_endian_memory()
    {
        var image = Harness.Assemble("""
            	db 'AB', 0x43
            """);

        //three bytes in source order, packed two per instruction with the first byte in the low half
        image.Select(b => (int)b).Should().Equal(0x41, 0x42, 0x43, 0x00);
    }

    [Fact]
    public void A_store_followed_by_byte_loads_sees_the_bytes_of_the_stored_word()
    {
        //stw [r1+0], r0 then ldb [r1+0] and ldb [r1+3], Imm(3) of LDB is a raw byte offset (doc/RiscA.MD line 91)
        var cpu = Harness.Cpu(Code.Stw(0, 1, 0), Code.Ldb(2, 1, 0), Code.Ldb(3, 1, 3), Code.Ret());
        cpu.With((0, unchecked((int)0xAABB_CCDD)), (1, (int)Ram + 0x400));

        cpu.Run();

        cpu.Space.ReadWord(Ram + 0x400).Should().Be(0xAABB_CCDD);
        //LDB zero extends, so a byte load is exactly one little endian lane of the word
        cpu.Regs[2].Should().Be(0xDD);
        cpu.Regs[3].Should().Be(0xAA);
    }

    [Fact]
    public void The_byte_lanes_of_a_device_register_are_little_endian_like_the_bus()
    {
        var fake = new FakeDevice("fake", 1);
        var space = Harness.Space();
        space.AddDevice("fake", 5, fake);
        uint block = 5 * 0x100;

        space.WriteByte(block + 3, 0xAA);
        space.WriteByte(block + 0, 0xDD);
        space.WriteByte(block + 1, 0xCC);
        space.WriteByte(block + 2, 0xBB);

        fake.Slots[0].Should().Be(0xAABB_CCDD);
        space.ReadHalfword(block).Should().Be(0xCCDD);
        space.ReadHalfword(block + 2).Should().Be(0xAABB);
        space.ReadWord(block).Should().Be(0xAABB_CCDD);

        //a halfword write keeps the other half of the register
        space.WriteHalfword(block + 2, 0x1234);
        space.ReadWord(block).Should().Be(0x1234_CCDD);
    }
}
