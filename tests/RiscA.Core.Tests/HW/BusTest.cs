using RiscA.Core.ISA;
using System;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

//The bus: one flat address space, every device decodes its own addresses (doc/emulator-architecture.md, §3 and §4).
public class BusTest
{
    private const uint Ram = Harness.Origin;

    [Fact]
    public void Words_in_ram_are_little_endian()
    {
        var bus = Harness.NewBus();
        bus.WriteWord(Ram + 0x10, 0x11223344);

        bus.ReadByte(Ram + 0x10).Should().Be(0x44);
        bus.ReadByte(Ram + 0x11).Should().Be(0x33);
        bus.ReadByte(Ram + 0x12).Should().Be(0x22);
        bus.ReadByte(Ram + 0x13).Should().Be(0x11);
    }

    [Fact]
    public void A_word_is_read_from_bytes_written_one_by_one()
    {
        var bus = Harness.NewBus();
        for (byte i = 0; i < 4; i++)
            bus.WriteByte(Ram + 0x20 + i, (byte)(0xAA + i));

        bus.ReadWord(Ram + 0x20).Should().Be(0xADACABAA);
    }

    [Fact]
    public void Ram_tolerates_unaligned_word_access()
    {
        var bus = Harness.NewBus();
        bus.WriteWord(Ram + 0x31, 0xDEADBEEF);

        bus.ReadWord(Ram + 0x31).Should().Be(0xDEADBEEF);
    }

    [Fact]
    public void Halfwords_are_the_16_bit_code_the_fetch_path_reads()
    {
        var bus = Harness.NewBus();
        bus.WriteHalfword(Ram, 0xBEEF);

        bus.ReadHalfword(Ram).Should().Be(0xBEEF);
        bus.ReadByte(Ram).Should().Be(0xEF);
    }

    [Fact]
    public void A_device_is_read_and_written_at_its_own_base()
    {
        var fake = new FakeDevice("fake", 0x0000_0100, 4, [0x11, 0x22, 0x33, 0x44]);
        var bus = Harness.NewBus();
        bus.AddDevice(fake);

        bus.ReadWord(0x0000_0104).Should().Be(0x22);
        fake.Written.Should().BeEmpty();

        bus.WriteWord(0x0000_0108, 0x99);
        fake.Written.Should().HaveCount(1);
        fake.Written[0].Should().Be((8, 0x99u));
        fake.Slots[2].Should().Be(0x99);
    }

    [Fact]
    public void A_byte_store_to_a_device_keeps_the_rest_of_the_register()
    {
        var fake = new FakeDevice("fake", 0x0000_0200, 1, [0xAABBCCDD]);
        var bus = Harness.NewBus();
        bus.AddDevice(fake);

        bus.WriteByte(0x0000_0202, 0x77);

        //a byte access is a read modify write of the whole register: the byte at offset 2 is the third little endian lane
        fake.Slots[0].Should().Be(0xAA77CCDD);
        fake.Written.Should().HaveCount(1);
        fake.Written[0].Should().Be((0, 0xAA77CCDDu));
    }

    [Fact]
    public void A_halfword_access_of_a_device_touches_the_register_it_lives_in()
    {
        var fake = new FakeDevice("fake", 0x0000_0300, 1, [0xAABBCCDD]);
        var bus = Harness.NewBus();
        bus.AddDevice(fake);

        //the bus composes a halfword out of two byte accesses, each one updates the whole register
        bus.WriteHalfword(0x0000_0302, 0x1234);

        fake.Slots[0].Should().Be(0x1234_CCDD);
        bus.ReadHalfword(0x0000_0300).Should().Be(0xCCDD);
    }

    [Fact]
    public void An_access_past_the_last_register_of_a_device_is_a_bus_fault()
    {
        var fake = new FakeDevice("fake", 0x0000_0400, 2);
        var bus = Harness.NewBus();
        bus.AddDevice(fake);

        var act = () => bus.ReadWord(0x0000_0408);

        //the device does not answer, so no device covers the address at all
        act.Should().Throw<Bus.Fault>()
            .Which.Kind.Should().Be(Bus.FaultKind.Unmapped);
    }

    [Fact]
    public void An_unaligned_word_access_of_a_device_is_a_bus_fault()
    {
        var fake = new FakeDevice("fake", 0x0000_0500, 4);
        var bus = Harness.NewBus();
        bus.AddDevice(fake);

        var act = () => bus.ReadWord(0x0000_0502);

        act.Should().Throw<Bus.Fault>()
            .Which.Kind.Should().Be(Bus.FaultKind.Unaligned);
    }

    [Theory]
    [InlineData(0x0000_8000)]                          //nothing answers in the empty low window
    [InlineData(Ram + 4096)]                           //past the end of the ram
    public void Addresses_outside_any_device_are_a_bus_fault(uint address)
    {
        var bus = Harness.NewBus(4096);

        var act = () => bus.ReadWord(address);

        act.Should().Throw<Bus.Fault>()
            .Which.Kind.Should().Be(Bus.FaultKind.Unmapped);
    }

    [Fact]
    public void The_fault_names_the_address_it_could_not_place()
    {
        var bus = Harness.NewBus(4096);

        var act = () => bus.WriteWord(Ram + 4094, 1);

        act.Should().Throw<Bus.Fault>()
            .WithMessage("*unmapped 4 byte write at 0x00010FFE*no device answers there*");
    }

    [Fact]
    public void Permissive_mode_reads_zero_and_drops_writes_of_unmapped_addresses()
    {
        var bus = Harness.NewBus(permissive: true);

        bus.ReadWord(0x0000_8000).Should().Be(0);
        bus.ReadByte(Ram + 999_999).Should().Be(0);
        bus.WriteByte(Ram + 999_999, 7);
        //the ram itself still works in permissive mode
        bus.WriteWord(Ram + 0x40, 5);
        bus.ReadWord(Ram + 0x40).Should().Be(5);
    }

    [Fact]
    public void The_device_added_first_answers_where_two_overlap()
    {
        var bus = Harness.NewBus();
        bus.AddDevice(new FakeDevice("small", 0x0000_0000, 1));
        bus.AddDevice(new FakeDevice("large", 0x0000_0000, 4));

        bus.RegionName(0x0000_0004).Should().Be("large");   //only the large one covers offset 4
        bus.RegionName(0x0000_0000).Should().Be("small");   //both cover offset 0, the first one wins
    }

    [Fact]
    public void A_rom_answers_reads_and_refuses_writes()
    {
        var rom = new Rom("rom", 0x0000_0200, [0x02, 0x2A, 0x87, 0x00]);
        var bus = Harness.NewBus();
        bus.AddDevice(rom);

        bus.ReadWord(0x0000_0200).Should().Be(0x0087_2A02);

        var act = () => bus.WriteWord(0x0000_0200, 0);
        act.Should().Throw<Bus.Fault>().Which.Kind.Should().Be(Bus.FaultKind.ReadOnly);

        act = () => bus.WriteByte(0x0000_0201, 0);
        act.Should().Throw<Bus.Fault>().WithMessage("*writing to rom 'rom'*");
    }

    [Theory]
    [InlineData(0x0000_0000, "dev0")]
    [InlineData(0x0000_000C, "dev0")]
    [InlineData(0x0000_000F, "dev0")]
    [InlineData(0x0000_0010, "unmapped")]
    [InlineData(0x0000_0100, "unmapped")]
    [InlineData(Ram, "ram")]
    [InlineData(Ram + 4092, "ram")]
    public void The_region_of_an_address_is_the_device_that_covers_it(uint address, string expected)
    {
        var bus = Harness.NewBus(4096);
        bus.AddDevice(new FakeDevice("dev0", 0x0000_0000, 4));

        bus.RegionName(address).Should().Be(expected);
    }

    [Fact]
    public void An_image_that_does_not_fit_is_rejected_before_anything_is_written()
    {
        var bus = Harness.NewBus(64);
        bus.WriteWord(Ram + 60, 0x11111111);

        var act = () => ProgramImage.Load(bus, Ram + 62, new byte[8]);

        act.Should().Throw<Bus.Fault>().WithMessage("*unmapped 8 byte*");
        bus.ReadWord(Ram + 60).Should().Be(0x11111111);
    }

    [Fact]
    public void Code_is_fetched_from_a_rom_as_well_as_from_ram()
    {
        //movi r0, 0x2A ; reti
        var bus = new Bus(new Bus.Options { ResetVector = 0x0000_0000 });
        bus.AddDevice(new Rom("rom", 0x0000_0000, [0x02, 0x2A, 0x87, 0x00]));
        var cpu = new CPU(bus);

        var movi = cpu.Step();
        var reti = cpu.Step();

        movi.Reason.Should().Be(StopReason.Running);
        cpu[0].Should().Be(0x2A);
        reti.Reason.Should().Be(StopReason.Reti);
        reti.Instruction.OpCode.Should().Be(OpCode.INT_RETI);
    }

    [Fact]
    public void Without_a_ram_or_a_reset_vector_the_cpu_has_nowhere_to_start()
    {
        var act = () => new CPU(new Bus());

        act.Should().Throw<InvalidOperationException>().WithMessage("*nowhere to start*");
    }

    [Fact]
    public void A_faulted_instruction_is_not_retired_and_reaches_no_device()
    {
        var fake = new FakeDevice("fake", 0x0000_0000, 1);
        var bus = Harness.NewBus();
        bus.AddDevice(fake);
        //ldw r0, [r0+0] with r1 = 0x1000: the word would span 0x1000..0x1003, nothing answers there
        ProgramImage.Load(bus, Harness.Origin, [Code.Ldw(0, 1, 0)]);
        var cpu = new CPU(bus);
        cpu[1] = 0x1000;

        var fault = cpu.Step();

        fault.Reason.Should().Be(StopReason.BusFault);
        //the PC stays on the instruction that faulted
        cpu.PC.Should().Be(Harness.Origin);
        cpu.InstructionsRetired.Should().Be(0);
        fake.Reads.Should().Be(0);
        fake.Writes.Should().Be(0);
    }
}
