using FluentAssertions;
using RiscA.Core.HW;
using Xunit;

namespace RiscA.Core.Tests.HW;

public class BreakpointTest
{
    //the image is loaded at the reset vector, so the instruction addresses are Harness.Origin + n
    const string Counting = """
        movi r0, 1
        movi r0, 2
        movi r0, 3
        reti
        """;

    private static CPU CountingWithBreakpointAt(uint offset)
    {
        var cpu = Harness.Program(Counting, origin: (int)Harness.Origin);
        cpu.AddBreakpoint(Harness.Origin + offset);
        cpu.Run().Reason.Should().Be(StopReason.Breakpoint);
        return cpu;
    }

    [Fact]
    public void Run_stops_at_a_breakpoint_before_executing_the_instruction_there()
    {
        var cpu = Harness.Program(Counting, origin: (int)Harness.Origin);
        cpu.AddBreakpoint(Harness.Origin + 4);

        var last = cpu.Run();

        last.Reason.Should().Be(StopReason.Breakpoint);
        last.Address.Should().Be(Harness.Origin + 4);
        cpu.PC.Should().Be(Harness.Origin + 4);
        cpu[0].Should().Be(2);
        cpu.Halted.Should().BeFalse();
    }

    [Fact]
    public void Removing_the_breakpoint_lets_the_program_run_to_its_end()
    {
        var cpu = CountingWithBreakpointAt(4);

        cpu.RemoveBreakpoint(Harness.Origin + 4).Should().BeTrue();
        var last = cpu.Run();

        last.Reason.Should().Be(StopReason.Reti);
        cpu[0].Should().Be(3);
    }

    [Fact]
    public void Run_from_a_breakpoint_address_runs_past_it_like_continue_does()
    {
        var cpu = CountingWithBreakpointAt(4);

        var last = cpu.Run();

        last.Reason.Should().Be(StopReason.Reti);
        cpu[0].Should().Be(3);
    }

    [Fact]
    public void Step_ignores_breakpoints_so_the_prompt_can_step_forward()
    {
        var cpu = CountingWithBreakpointAt(4);

        var step = cpu.Step();

        step.Address.Should().Be(Harness.Origin + 4);
        step.IsRunning.Should().BeTrue();
        cpu[0].Should().Be(3);
        cpu.HasBreakpoint(Harness.Origin + 4).Should().BeTrue();
    }

    [Fact]
    public void A_breakpoint_in_a_loop_stops_on_every_pass_after_the_first()
    {
        var cpu = Harness.Program("""
            loop:
                add r0, 1
                j loop
            """, origin: (int)Harness.Origin);
        cpu.AddBreakpoint(Harness.Origin + 2);

        cpu.Run(500).Reason.Should().Be(StopReason.Breakpoint);
        cpu[0].Should().Be(1);

        cpu.Run(500).Reason.Should().Be(StopReason.Breakpoint);
        cpu[0].Should().Be(2);
    }
}
