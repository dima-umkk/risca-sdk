using RiscA.Core.ISA;
using System;
using System.Collections.Generic;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

//The bus: one flat address bus, devices in the low window, RAM above it (doc/emulator-architecture.md, §3 and §4).
public class BusTest
{
    private const uint Ram = BusOptions.DeviceWindowEnd;

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
    public void A_device_block_is_decoded_by_its_own_device()
    {
        var fake = new FakeDevice("fake", 4, [0x11, 0x22, 0x33, 0x44]);
        var bus = Harness.NewBus();
        bus.AddDevice("fake", 1, fake);

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
        var fake = new FakeDevice("fake", 1, [0xAABBCCDD]);
        var bus = Harness.NewBus();
        bus.AddDevice("fake", 2, fake);

        bus.WriteByte(0x0000_0202, 0x77);

        //a byte access is a read modify write of the whole register: the byte at offset 2 is the third little endian lane
        fake.Slots[0].Should().Be(0xAA77CCDD);
        fake.Written.Should().HaveCount(1);
        fake.Written[0].Should().Be((0, 0xAA77CCDDu));
    }

    [Fact]
    public void A_halfword_access_of_a_device_touches_the_register_it_lives_in()
    {
        var fake = new FakeDevice("fake", 1, [0xAABBCCDD]);
        var bus = Harness.NewBus();
        bus.AddDevice("fake", 3, fake);

        bus.WriteHalfword(0x0000_0302, 0x1234);

        fake.Slots[0].Should().Be(0x1234CCDD);
        bus.ReadHalfword(0x0000_0300).Should().Be(0xCCDD);
    }

    [Fact]
    public void Reaching_past_the_last_register_of_a_device_is_a_bus_fault()
    {
        var fake = new FakeDevice("fake", 2);
        var bus = Harness.NewBus();
        bus.AddDevice("fake", 4, fake);

        var act = () => bus.ReadWord(0x0000_0408);

        act.Should().Throw<BusFault>()
            .Which.Kind.Should().Be(BusFaultKind.DeviceRange);
    }

    [Fact]
    public void An_unaligned_word_access_of_a_device_is_a_bus_fault()
    {
        var fake = new FakeDevice("fake", 4);
        var bus = Harness.NewBus();
        bus.AddDevice("fake", 5, fake);

        var act = () => bus.ReadWord(0x0000_0502);

        act.Should().Throw<BusFault>()
            .Which.Kind.Should().Be(BusFaultKind.Unaligned);
    }

    [Theory]
    [InlineData(0x0000_8000)]                          //an empty device block inside the window
    [InlineData(Ram - 4)]                              //the last word of the window, no device there
    [InlineData(Ram + 4096)]                           //past the end of the ram
    public void Addresses_outside_any_region_are_a_bus_fault(uint address)
    {
        var bus = Harness.NewBus(4096);

        var act = () => bus.ReadWord(address);

        act.Should().Throw<BusFault>()
            .Which.Kind.Should().Be(BusFaultKind.Unmapped);
    }

    [Fact]
    public void The_fault_names_the_address_and_the_regions_it_tried()
    {
        var bus = Harness.NewBus(4096);
        bus.AddDevice("uart0", 0, new FakeDevice("uart0"));

        var act = () => bus.WriteWord(Ram + 4094, 1);

        act.Should().Throw<BusFault>()
            .WithMessage("*unmapped 4 byte write at 0x00010FFE*devices: 0x00000000..0x0000FFFF, ram: 0x00010000..0x00010FFF*");
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
    public void Two_devices_can_not_share_a_block()
    {
        var bus = Harness.NewBus();
        bus.AddDevice("uart0", 0, new FakeDevice("uart0"));

        var act = () => bus.AddDevice("other", 0, new FakeDevice("other"));

        act.Should().Throw<ArgumentException>().WithMessage("*overlaps device 'uart0'*");
    }

    [Fact]
    public void A_device_can_not_live_outside_the_device_window()
    {
        var bus = Harness.NewBus();

        var act = () => bus.AddDevice("far", 0x100, new FakeDevice("far"));

        act.Should().Throw<ArgumentException>().WithMessage("*does not fit into the device window*");
    }

    [Fact]
    public void A_device_can_not_be_larger_than_the_256_byte_block()
    {
        var bus = Harness.NewBus();

        var act = () => bus.AddDevice("huge", 1, new FakeDevice("huge", 65)); //65 registers = 260 bytes, past the block

        act.Should().Throw<ArgumentException>().WithMessage("*has to declare between 4 and 256 bytes*");
    }

    [Fact]
    public void The_ram_can_not_start_inside_the_device_window()
    {
        var act = () => new Bus(new BusOptions { RamBase = 0x8000 });

        act.Should().Throw<ArgumentException>().WithMessage("*has to start at 0x00010000 or higher*");
    }

    [Theory]
    [InlineData(0x0000_0000, "uart0")]
    [InlineData(0x0000_00FC, "uart0")]
    [InlineData(0x0000_00FF, "uart0")]
    [InlineData(0x0000_0100, "unmapped")]
    [InlineData(0x0000_FFFC, "unmapped")]
    [InlineData(Ram, "ram")]
    [InlineData(Ram + 4092, "ram")]
    public void The_region_of_an_address_follows_the_decode_rule(uint address, string expected)
    {
        var bus = Harness.NewBus(4096);
        bus.AddDevice("uart0", 0, new FakeDevice("uart0"));

        bus.RegionName(address).Should().Be(expected);
    }

    [Fact]
    public void Only_the_high_address_half_decides_between_the_device_bus_and_the_ram()
    {
        //a ram placed away from the window: addresses above 0x0000FFFF still belong to it
        var bus = new Bus(new BusOptions { RamBase = 0x0002_0000, RamSize = 0x1000 });
        bus.AddDevice("edge", 255, new FakeDevice("edge"));

        bus.WriteWord(0x0000_FF00, 0x1234);       //block 255 is the last block of the window
        bus.ReadWord(0x0000_FF00).Should().Be(0x1234);
        bus.RegionName(0x0000_FFFC).Should().Be("edge");   //the whole block of the device is decoded
        bus.RegionName(0x0001_0000).Should().Be("unmapped");

        bus.WriteWord(0x0002_0000, 7);
        bus.ReadWord(0x0002_0000).Should().Be(7);
        bus.RegionName(0xFFFF_FFFC).Should().Be("unmapped");
    }

    [Fact]
    public void An_image_that_does_not_fit_is_rejected_before_anything_is_written()
    {
        var bus = Harness.NewBus(64);
        bus.WriteWord(Ram + 60, 0x11111111);

        var act = () => ProgramImage.Load(bus, Ram + 62, new byte[8]);

        act.Should().Throw<BusFault>().WithMessage("*unmapped 8 byte*");
        bus.ReadWord(Ram + 60).Should().Be(0x11111111);
    }

    [Fact]
    public void Code_is_fetched_from_a_device_block_as_well_as_from_ram()
    {
        //movi r0, 0x2A ; reti, both instructions inside one 32 bit register of a rom like device
        var rom = new RomDevice("rom", 0x2A02, 0x0087);
        //the model resets to the start of the ram, so the test sends the PC to the rom block by hand
        var cpu = new CPU(Harness.NewBus());
        cpu.Bus.AddDevice("rom", 0, rom);
        cpu.PC = 0x0000_0000;

        var movi = cpu.Step();
        var reti = cpu.Step();

        movi.Reason.Should().Be(StopReason.Running);
        cpu.Regs[0].Should().Be(0x2A);
        reti.Reason.Should().Be(StopReason.Reti);
        reti.Instruction.OpCode.Should().Be(OpCode.INT_RETI);
    }

    [Fact]
    public void Every_device_ticks_once_per_retired_instruction()
    {
        var one = new FakeDevice("one");
        var two = new FakeDevice("two");
        var cpu = new CPU(Harness.NewBus());
        cpu.Bus.AddDevice("one", 1, one).AddDevice("two", 2, two);
        //the empty ram reads as NOPs, so the machine retires instructions from the reset vector on

        cpu.Step();
        cpu.Step();

        one.Ticks.Should().Be(2);
        two.Ticks.Should().Be(2);
        cpu.InstructionsRetired.Should().Be(2);
    }

    [Fact]
    public void A_faulted_instruction_is_not_retired_and_does_not_tick_the_devices()
    {
        var fake = new FakeDevice("fake", 1);
        var cpu = new CPU(Harness.NewBus());
        cpu.Bus.AddDevice("fake", 1, fake);
        //ldw r0, [r0+0] reads address 0, an empty device block
        ProgramImage.Load(cpu.Bus, cpu.PC, [Code.Ldw(0, 0, 0)]);

        var fault = cpu.Step();

        fault.Reason.Should().Be(StopReason.BusFault);
        //the PC stays on the instruction that faulted
        cpu.PC.Should().Be(Harness.Origin);
        cpu.InstructionsRetired.Should().Be(0);
        fake.Ticks.Should().Be(0);
    }
}
