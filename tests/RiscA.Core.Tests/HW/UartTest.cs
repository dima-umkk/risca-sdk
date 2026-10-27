using FluentAssertions;
using System;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

//the console uart of the model: four word registers at the bottom of the address space, as samples/lib/uart.rasm expects
public class UartTest
{
    //a machine with only this uart in it: the tests poke the registers through the bus, as LDW/STW of a program do
    private static (CPU cpu, UartDevice uart, ScriptConsole console) Rig(ScriptConsole? console = null)
    {
        var cpu = new CPU(Harness.NewBus());
        console ??= new ScriptConsole();
        var uart = cpu.AddUart(console);
        return (cpu, uart, console);
    }

    //the address of a register of the uart: uart0 sits at address 0, word slot N is byte 4N
    private static uint Addr(int slot) => UartDevice.DefaultBase + (uint)(slot * 4);

    [Fact]
    public void Tx_data_sends_the_low_byte_of_the_word_to_the_console()
    {
        var (cpu, _, console) = Rig();

        cpu.Bus.WriteWord(Addr(UartDevice.TxDataSlot), 'H');
        //only the low byte is the character, the rest of the word is ignored
        cpu.Bus.WriteWord(Addr(UartDevice.TxDataSlot), 0x120069u);

        console.Output.GetBuffer().AsSpan(0, 2).ToArray().Should().Equal((byte)'H', (byte)'i');
        console.Text.Should().Be("Hi");
    }

    [Fact]
    public void Tx_ready_is_always_one_because_the_transmitter_never_sleeps()
    {
        var (cpu, uart, console) = Rig();

        for (char c = 'a'; c <= 'z'; c++)
        {
            cpu.Bus.ReadWord(Addr(UartDevice.TxReadySlot)).Should().Be(1u, $"{c} may go right away");
            cpu.Bus.WriteWord(Addr(UartDevice.TxDataSlot), c);
        }
        console.Text.Should().Be("abcdefghijklmnopqrstuvwxyz");
        uart.RxCount.Should().Be(0);
    }

    [Fact]
    public void Rx_ready_shows_the_byte_and_rx_data_takes_it_out_of_the_fifo()
    {
        var console = new ScriptConsole().Feed("H");
        var (cpu, _, _) = Rig(console);

        cpu.Bus.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(1u, "looking at rx_ready pulls the console byte");
        cpu.Bus.ReadWord(Addr(UartDevice.RxDataSlot)).Should().Be((uint)'H');
        //reading pops: the second read of an empty fifo reads as 0, not as the same byte again
        cpu.Bus.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(0u);
        cpu.Bus.ReadWord(Addr(UartDevice.RxDataSlot)).Should().Be(0u);
    }

    [Fact]
    public void Rx_bytes_arrive_one_per_look_of_the_program()
    {
        var console = new ScriptConsole().Feed("abc");
        var (cpu, uart, _) = Rig(console);

        for (char expected = 'a'; expected <= 'c'; expected++)
        {
            cpu.Bus.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(1u);
            uart.RxCount.Should().Be(1, "one look pulls one byte, no more");
            cpu.Bus.ReadWord(Addr(UartDevice.RxDataSlot)).Should().Be((uint)expected);
        }
        //the stream ends after the scripted bytes, and an ended input stays ended
        cpu.Bus.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(0u);
        cpu.Bus.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(0u);
    }

    [Fact]
    public void Writing_rx_data_injects_a_byte_of_input_for_the_program()
    {
        var (cpu, uart, _) = Rig();

        //this is the scripted input of the tests, the same way the keyboard of a real board feeds the uart
        cpu.Bus.WriteWord(Addr(UartDevice.RxDataSlot), 'k');
        cpu.Bus.WriteWord(Addr(UartDevice.RxDataSlot), 0x0000_0A00u);   //only the low byte counts

        uart.RxCount.Should().Be(2);
        cpu.Bus.ReadWord(Addr(UartDevice.RxDataSlot)).Should().Be((uint)'k');
        cpu.Bus.ReadWord(Addr(UartDevice.RxDataSlot)).Should().Be(0u);
    }

    [Fact]
    public void Injected_input_stops_at_the_fifo_depth_of_sixteen()
    {
        var (cpu, uart, _) = Rig();

        for (uint i = 0; i < 100; i++)
            cpu.Bus.WriteWord(Addr(UartDevice.RxDataSlot), i);

        uart.RxCount.Should().Be(UartDevice.RxFifoDepth, "a full fifo is backpressure, the rest is dropped");
    }

    [Fact]
    public void A_uart_without_an_input_stream_is_simply_never_ready()
    {
        var bus = Harness.NewBus();
        bus.AddDevice(new UartDevice(output: new System.IO.MemoryStream()));

        bus.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(0u);
        bus.ReadWord(Addr(UartDevice.RxReadySlot)).Should().Be(0u);
    }

    [Fact]
    public void A_byte_written_to_tx_data_as_a_byte_lands_in_the_byte_lane_of_the_register()
    {
        var (cpu, _, console) = Rig();

        //the trap: STB composes the register but the uart only looks at the low byte, so characters go through STW like in samples/lib/uart.rasm
        cpu.Bus.WriteByte(Addr(UartDevice.TxDataSlot) + 2, (byte)'A');
        console.Output.GetBuffer().AsSpan(0, 1).ToArray().Should().Equal((byte)0);

        cpu.Bus.WriteWord(Addr(UartDevice.TxDataSlot), 0x00410000u);
        console.Output.GetBuffer().AsSpan(0, 2).ToArray().Should().Equal((byte)0, (byte)0);
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
}
