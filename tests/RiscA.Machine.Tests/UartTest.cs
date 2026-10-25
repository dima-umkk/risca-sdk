using FluentAssertions;
using System;

namespace RiscA.Machine.Tests;

//The console uart of the model: four registers in device block 0, exactly as
//"doc/emulator-architecture.md", §5 agreed it, and the samples/lib/uart.rasm library expects it.
public class UartTest
{
    //a machine with only the uart in it, no code: the tests talk to the registers through the bus,
    //which is what LDW/STW of a running program do
    private static (Machine cpu, UartDevice uart, ScriptConsole console) Rig(int txCyclesPerByte = 0)
    {
        var cpu = new Machine(Harness.Space());
        var console = new ScriptConsole();
        var uart = cpu.AddUart(console, txCyclesPerByte);
        return (cpu, uart, console);
    }

    //the address of a register of the uart: block 0, word slot N
    private static uint Addr(int slot) => (uint)(UartDevice.Block * 0x100 + slot * 4);

    [Fact]
    public void Tx_data_sends_the_low_byte_of_the_word_to_the_console()
    {
        var (cpu, _, console) = Rig();

        cpu.Space.WriteWord(Addr(UartDevice.TxDataSlot), 'H');
        //only the low byte is the character, the rest of the word is ignored
        cpu.Space.WriteWord(Addr(UartDevice.TxDataSlot), 0x120069u);

        console.Output.Should().Equal((int)'H', (int)'i');
        console.Text.Should().Be("Hi");
    }

    [Fact]
    public void Tx_ready_is_one_as_long_as_the_transmitter_never_sleeps()
    {
        var (cpu, uart, console) = Rig();

        for (char c = 'a'; c <= 'z'; c++)
        {
            cpu.Space.ReadWord(Addr(UartDevice.TxReadySlot)).Should().Be(1u, $"{c} may go right away");
            cpu.Space.WriteWord(Addr(UartDevice.TxDataSlot), c);
        }
        console.Text.Should().Be("abcdefghijklmnopqrstuvwxyz");
        uart.RxAvailable.Should().BeFalse();
    }

    [Fact]
    public void With_a_transmitter_delay_tx_ready_goes_low_and_a_byte_written_too_early_is_lost()
    {
        var (cpu, uart, console) = Rig(txCyclesPerByte: 2);

        cpu.Space.WriteWord(Addr(UartDevice.TxDataSlot), 'a');
        cpu.Space.ReadWord(Addr(UartDevice.TxReadySlot)).Should().Be(0u, "the byte is still on the wire");
        //the library polls tx_ready first, a program that does not loses a character instead of
        //having it quietly queued, so the missing character shows up in the output
        cpu.Space.WriteWord(Addr(UartDevice.TxDataSlot), 'b');
        console.Text.Should().Be("a");

        uart.Tick();
        cpu.Space.ReadWord(Addr(UartDevice.TxReadySlot)).Should().Be(0u);
        uart.Tick();
        cpu.Space.ReadWord(Addr(UartDevice.TxReadySlot)).Should().Be(1u);
        cpu.Space.WriteWord(Addr(UartDevice.TxDataSlot), 'c');

        console.Text.Should().Be("ac");
    }

    [Fact]
    public void Rx_ready_shows_the_byte_and_rx_data_takes_it_out_of_the_fifo()
    {
        var (cpu, uart, console) = Rig();

        cpu.Space.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(0u);
        console.Feed("H");
        uart.Tick();

        cpu.Space.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(1u);
        cpu.Space.ReadWord(Addr(UartDevice.RxDataSlot)).Should().Be((uint)'H');
        //reading pops: the second read of an empty fifo reads as 0, not as the same byte again
        cpu.Space.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(0u);
        cpu.Space.ReadWord(Addr(UartDevice.RxDataSlot)).Should().Be(0u);
    }

    [Fact]
    public void Writing_rx_data_injects_a_byte_of_input_for_the_program()
    {
        var (cpu, uart, _) = Rig();

        //this is the scripted input of the tests, the same way the keyboard of a real board feeds us
        cpu.Space.WriteWord(Addr(UartDevice.RxDataSlot), 'k');
        cpu.Space.WriteWord(Addr(UartDevice.RxDataSlot), 0x0000_0A00u);   //only the low byte counts

        uart.RxCount.Should().Be(2);
        cpu.Space.ReadWord(Addr(UartDevice.RxDataSlot)).Should().Be((uint)'k');
        cpu.Space.ReadWord(Addr(UartDevice.RxDataSlot)).Should().Be(0u);
    }

    [Fact]
    public void One_byte_of_the_console_arrives_per_retired_instruction_and_the_fifo_holds_sixteen()
    {
        var (cpu, uart, console) = Rig();

        console.Feed("abc");
        uart.Tick();
        uart.RxCount.Should().Be(1);
        uart.Tick();
        uart.Tick();
        uart.RxCount.Should().Be(3);
        uart.Tick();
        uart.RxCount.Should().Be(3, "the console has nothing else to give");

        console.Feed(new string('x', 100));
        for (int i = 0; i < 100; i++)
            uart.Tick();
        uart.RxCount.Should().Be(UartDevice.RxFifoDepth, "a full fifo is backpressure, the rest waits in the console");
        uart.RxAvailable.Should().BeTrue();
    }

    [Fact]
    public void Polling_an_empty_fifo_is_counted_and_the_next_byte_clears_it()
    {
        var (cpu, uart, console) = Rig();

        cpu.Space.ReadWord(Addr(UartDevice.RxReadySlot));
        cpu.Space.ReadWord(Addr(UartDevice.RxReadySlot));
        uart.EmptyReadyReads.Should().Be(2);

        console.Feed("z");
        uart.Tick();
        uart.EmptyReadyReads.Should().Be(0, "a byte arrived, the wait is over");
        cpu.Space.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(1u);
        uart.EmptyReadyReads.Should().Be(0);
    }

    [Fact]
    public void A_byte_written_to_tx_data_as_a_byte_lands_in_the_byte_lane_of_the_register()
    {
        var (cpu, _, console) = Rig();

        //the trap worth knowing: STB composes the register, the uart still only looks at its low
        //byte, so the character has to be written with STW, which is what samples/lib/uart.rasm does
        cpu.Space.WriteByte(Addr(UartDevice.TxDataSlot) + 2, (byte)'A');
        console.Output.Should().Equal(0);

        cpu.Space.WriteWord(Addr(UartDevice.TxDataSlot), 0x00410000u);
        console.Output.Should().Equal(0, 0);
    }

    [Fact]
    public void The_echo_sample_prints_its_greeting_and_echoes_the_key_back()
    {
        var console = new ScriptConsole().Feed("k");
        var (cpu, _) = Harness.Sample("uart.echo.rasm", console);

        //the limit is there so a program that loops stops the test instead of hanging the runner
        cpu.Run(200_000).Reason.Should().Be(StopReason.Reti);
        console.Text.Should().Be("Hi!\n?k\n");
        cpu.InstructionsRetired.Should().BeGreaterThan(40);
    }

    [Fact]
    public void The_echo_sample_works_through_a_slow_transmitter_as_well_because_the_library_waits()
    {
        var console = new ScriptConsole().Feed("k");
        var (cpu, _) = Harness.Sample("uart.echo.rasm", console, txCyclesPerByte: 200);

        cpu.Run(200_000).Reason.Should().Be(StopReason.Reti);
        console.Text.Should().Be("Hi!\n?k\n");
        //waiting for the wire costs instructions: the library waits for the transmitter between
        //the characters, and every wait is about txCyclesPerByte retired instructions long. The
        //last character leaves the program on its own, so six of the seven are waited for.
        cpu.InstructionsRetired.Should().BeGreaterThan(1000,
            "six of the seven characters wait for the wire, about txCyclesPerByte retired instructions each");
    }
}
