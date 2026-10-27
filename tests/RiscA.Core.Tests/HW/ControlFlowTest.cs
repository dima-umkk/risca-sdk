using RiscA.Core.ISA;
using System;
using System.Collections.Generic;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

//Opcodes 4, 6 and 7: branches, calls and the stop convention of the model (doc/RiscA.MD lines 98-148).
public class ControlFlowTest
{
    private const uint Origin = Harness.Origin;

    [Fact]
    public void Beqz_jumps_relative_to_the_branch_itself_in_instructions()
    {
        //PC = PC + signed(Imm(8)) << 1 (doc/RiscA.MD line 104)
        var cpu = Harness.Cpu(Code.Beqz(1, 2), Code.Nop, Code.Ret());

        cpu.Step();

        cpu.PC.Should().Be(Origin + 2 * 2);
    }

    [Fact]
    public void Beqz_falls_through_when_the_register_is_not_zero()
    {
        var cpu = Harness.Cpu(Code.Beqz(1, 2), Code.Nop, Code.Ret()).With((1, 7));

        cpu.Step();

        cpu.PC.Should().Be(Origin + 2);
    }

    [Fact]
    public void Bnez_jumps_backwards_over_the_loop_body()
    {
        var cpu = Harness.Cpu(Code.Nop, Code.Nop, Code.Nop, Code.Bnez(1, -2)).With((1, 1));

        for (var i = 0; i < 4; i++)
            cpu.Step();

        cpu.PC.Should().Be(Origin + 2);
    }

    [Fact]
    public void A_counted_loop_terminates_and_the_branch_goes_back_over_its_own_address()
    {
        //three iterations of a two instruction loop, then the branch falls through to RETI
        var cpu = Harness.Cpu(Code.Movi(1, 3), Code.Subi(1, 1), Code.Bnez(1, -1), Code.Reti());

        var last = cpu.Run();

        last.Reason.Should().Be(StopReason.Reti);
        cpu[1].Should().Be(0);
        cpu.InstructionsRetired.Should().Be(8);
    }

    [Fact]
    public void Call_saves_the_return_address_in_R14_and_counts_the_target_in_words()
    {
        //CALL: PC = PC + signed(Imm(11)) << 2, functions are aligned to 4 bytes (doc/RiscA.MD line 124)
        var cpu = Harness.Cpu(Code.Call(2), Code.Ret());

        cpu.Step();

        cpu.PC.Should().Be(Origin + 2 * 4);
        cpu[CPU.LinkRegister].Should().Be(Origin + 2);
    }

    [Fact]
    public void Call_reaches_backwards_as_well_as_forwards()
    {
        //the target is the word the PC sits in plus imm words, so from Origin + 6 one word back is Origin
        var cpu = Harness.Cpu(Code.Ret(), Code.Ret(), Code.Ret(), Code.Call(-1));
        cpu.PC = Origin + 6;

        cpu.Step();

        cpu.PC.Should().Be(Origin);
        cpu[14].Should().Be(Origin + 8);
    }

    [Fact]
    public void Call_with_a_register_jumps_there_and_links_to_the_next_instruction()
    {
        var cpu = Harness.Cpu(Code.CallReg(3), Code.Ret()).With((3, (int)(Origin + 0x40)));

        cpu.Step();

        cpu.PC.Should().Be(Origin + 0x40);
        cpu[14].Should().Be(Origin + 2);
    }

    [Fact]
    public void Ret_returns_to_the_link_register()
    {
        var cpu = Harness.Cpu(Code.Ret()).With((14, (int)(Origin + 8)));

        cpu.Step();

        cpu.PC.Should().Be(Origin + 8);
    }

    [Fact]
    public void Ret_from_another_register_is_the_jmp_of_the_spec()
    {
        //doc/RiscA.MD line 126: JMP Rd is the RET Rd encoding, the disassembly prints it as JMP
        Code.Ret(3).ToString().Should().Be("JMP  R3");

        var cpu = Harness.Cpu(Code.Ret(3)).With((3, (int)(Origin + 0x20)));

        cpu.Step();

        cpu.PC.Should().Be(Origin + 0x20);
        cpu[14].Should().Be(0); //JMP does not touch the link register
    }

    [Theory]
    [InlineData(3, 3)]      //forward, counted in instructions
    [InlineData(-3, -3)]    //backwards
    [InlineData(0, 0)]      //zero is the endless loop of jr onto itself
    public void Jr_offset_is_counted_in_instructions_not_in_words(int offset, int expected)
    {
        var cpu = Harness.Cpu(Code.Jr(offset), Code.Ret());

        cpu.Step();

        cpu.PC.Should().Be(Origin + (uint)(expected * 2));
    }

    [Fact]
    public void Reti_ends_the_program_by_convention()
    {
        //interrupts are not modelled, so RETI is the agreed way for a program to end (doc/emulator-architecture.md, §6)
        var cpu = Harness.Cpu(Code.Reti());

        var last = cpu.Step();

        last.Reason.Should().Be(StopReason.Reti);
        last.Message.Should().Be("RETI: the program ended (stop convention of the model)");
        cpu.Halted.Should().BeTrue();
    }

    [Fact]
    public void Int_stops_with_a_diagnostic_because_interrupts_are_not_modelled_yet()
    {
        var cpu = Harness.Cpu(Code.Int(0));

        var last = cpu.Step();

        last.Reason.Should().Be(StopReason.IntNotImplemented);
        last.Message.Should().StartWith("INT R0: interrupts and syscalls are not implemented");
        cpu.Halted.Should().BeTrue();
    }

    [Fact]
    public void Stepping_a_halted_machine_changes_nothing()
    {
        var cpu = Harness.Cpu(Code.Reti());
        cpu.Run();

        var step = cpu.Step();

        step.Reason.Should().Be(StopReason.Halted);
        step.Message.Should().Be("the machine is halted, Step() does nothing until Reset()");
        step.Address.Should().Be(Origin);
        cpu.InstructionsRetired.Should().Be(1);
        cpu.PC.Should().Be(Origin + 2);
    }

    [Fact]
    public void Reset_brings_the_machine_back_to_the_reset_vector()
    {
        var cpu = Harness.Cpu(Code.Movi(0, 7), Code.Ret());
        cpu.Run();

        cpu.Reset();

        cpu.PC.Should().Be(Origin);
        cpu.Halted.Should().BeFalse();
        cpu.InstructionsRetired.Should().Be(0);
        cpu[0].Should().Be(0);
        cpu[14].Should().Be(0);
        cpu.EPC.Should().Be(0);
    }

    [Fact]
    public void Epc_is_a_plain_register_the_program_can_read_and_write()
    {
        //MOV EPC, Rd and MOV Rd, EPC (doc/RiscA.MD line 148), 0xF0 fits in the 8 bit immediate of MOVI
        var cpu = Harness.Cpu(Code.Movi(1, 0xF0), Code.SetEpc(1), Code.GetEpc(2), Code.Ret());
        cpu.With((2, 0x999));

        cpu.Run(3);

        cpu.EPC.Should().Be(0xF0);
        //MOV R2, EPC overwrote the preset value of R2
        cpu[2].Should().Be(0xF0);
        cpu[1].Should().Be(0xF0);
    }

    [Fact]
    public void Run_stops_after_the_requested_number_of_instructions()
    {
        //jr 0 jumps onto itself (doc/README.md, opcode 6)
        var cpu = Harness.Cpu(Code.Nop, Code.Jr(0));
        cpu.PC = Origin + 2;

        var last = cpu.Run(100);

        last.Reason.Should().Be(StopReason.InstructionLimit);
        last.Message.Should().Be("stopped after 100 instructions");
        cpu.InstructionsRetired.Should().Be(100);
        cpu.Halted.Should().BeFalse();
    }

    [Fact]
    public void A_run_without_a_limit_ends_when_the_program_stops()
    {
        var cpu = Harness.Cpu(Code.Reti());

        cpu.Run();

        cpu.Halted.Should().BeTrue();
        cpu.InstructionsRetired.Should().Be(1);
    }

    [Fact]
    public void A_fetch_from_an_odd_address_stops_with_a_bus_fault()
    {
        var cpu = Harness.Cpu(Code.Movi(0, 1), Code.Ret());
        cpu.PC = Origin + 3;

        var last = cpu.Step();

        last.Reason.Should().Be(StopReason.BusFault);
        last.Message.Should().Be("the instruction fetch at 0x00010003 is not aligned to 2 bytes");
        cpu.Halted.Should().BeTrue();
        cpu.InstructionsRetired.Should().Be(0);
    }

    [Fact]
    public void The_trace_hook_sees_every_retired_instruction()
    {
        var trace = new List<string>();
        var cpu = Harness.Cpu(Code.Movi(0, 1), Code.Addi(0, 1), Code.Ret());
        cpu.OnRetire = step => trace.Add($"{step.Address:X8} {step.Instruction}");

        cpu.Run();

        trace.Should().Equal(
            $"{Origin:X8} MOVI R0, 1",
            $"{Origin + 2:X8} ADD  R0, 1",
            $"{Origin + 4:X8} RET");
    }

    [Fact]
    public void A_step_result_prints_the_listing_line_the_cli_wants()
    {
        var cpu = Harness.Cpu(Code.Addi(0, 5));

        cpu.Step().ToString().Should().Be($"0x{Origin:X8} 0B01  ADD  R0, 5");
    }
}
