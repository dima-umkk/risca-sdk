using FluentAssertions;
using RiscA.Core.ISA;
using System;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

//rasim has to notice a program polling for a key after the input of the process has closed (doc/emulator-architecture.md, §5)
public class InputWaitDetectorTest
{
    //run the machine until the detector has an opinion, exactly what the rasim loop does
    private static StepResult RunWithDetector(CPU cpu, InputWaitDetector detector, int maxSteps = 100_000)
    {
        var last = cpu.Last;
        for (int i = 0; i < maxSteps && !cpu.Halted; i++)
        {
            last = cpu.Step();
            if (detector.Stuck)
                break;
        }
        return last;
    }

    [Fact]
    public void A_program_polling_for_a_key_on_a_closed_input_is_reported_as_waiting()
    {
        var console = new ScriptConsole().End();
        var (cpu, uart) = Harness.Sample("uart.echo.rasm", console);
        var detector = new InputWaitDetector(uart, console, patience: 100);

        var last = RunWithDetector(cpu, detector);

        detector.Stuck.Should().BeTrue();
        //the cpu itself is fine, it is the host that stops the loop and says why
        last.Reason.Should().Be(StopReason.Running);
        cpu.Halted.Should().BeFalse();
        console.Text.Should().Be("Hi!\n?", "the greeting went out, the key never came");
        uart.EmptyReadyReads.Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public void A_wait_while_the_user_might_still_type_is_a_normal_wait()
    {
        var console = new ScriptConsole();      //interactive: the input has not ended
        var (cpu, uart) = Harness.Sample("uart.echo.rasm", console);
        var detector = new InputWaitDetector(uart, console, patience: 20);

        for (int i = 0; i < 20_000 && !cpu.Halted; i++)
        {
            cpu.Step();
            detector.Stuck.Should().BeFalse($"an interactive session may wait forever, step {i}");
        }

        cpu.Halted.Should().BeFalse();
        console.Text.Should().Be("Hi!\n?");
        uart.EmptyReadyReads.Should().BeGreaterThan(1000);
    }

    [Fact]
    public void A_key_that_arrives_late_clears_the_counter_and_the_program_finishes()
    {
        var console = new ScriptConsole();
        var (cpu, uart) = Harness.Sample("uart.echo.rasm", console);
        var detector = new InputWaitDetector(uart, console, patience: 500);

        for (int steps = 0; !cpu.Halted && uart.EmptyReadyReads < 400 && steps < 20_000; steps++)
        {
            cpu.Step();
            detector.Stuck.Should().BeFalse();
        }
        console.Feed("k");

        cpu.Run(200_000).Reason.Should().Be(StopReason.Reti);
        detector.Stuck.Should().BeFalse();
        console.Text.Should().Be("Hi!\n?k\n");
    }

    [Fact]
    public void A_delay_loop_that_never_touches_the_uart_is_not_reported_as_a_console_wait()
    {
        var console = new ScriptConsole().End();
        var cpu = Harness.Program("""
            movi r0, 200
            loop:
                sub r0, 1
                shl r0, 0
                shr r0, 0
                bnez r0, loop
            reti
            """);
        var uart = cpu.AddUart(console);
        var detector = new InputWaitDetector(uart, console, patience: 100);

        var last = RunWithDetector(cpu, detector);

        last.Reason.Should().Be(StopReason.Reti);
        detector.Stuck.Should().BeFalse("a busy loop is not a console wait, it never read rx_ready");
        uart.EmptyReadyReads.Should().Be(0);
    }

    [Fact]
    public void The_detector_needs_both_an_ended_input_and_enough_empty_polls()
    {
        var console = new ScriptConsole().Feed("abc").End();
        var cpu = Harness.Program("""
            movi r7, 0
            poll:
                ldw r0, [r7+1]
                beqz r0, poll
                ldw r1, [r7+0]
            reti
            """);
        var uart = cpu.AddUart(console);
        var detector = new InputWaitDetector(uart, console, patience: 500);

        //the fifo holds 'a', so the very first poll of rx_ready succeeds: nothing to report
        RunWithDetector(cpu, detector).Reason.Should().Be(StopReason.Reti);
        detector.Stuck.Should().BeFalse();
        uart.EmptyReadyReads.Should().Be(0);
    }
}
