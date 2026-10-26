using RiscA.Core.ISA;
using System;
using System.IO;

namespace RiscA.Machine.Tests;

//Whole programs: the real assembler in front, the real machine behind (M0 acceptance).
public class RunTest
{
    private const uint Ram = Harness.Origin;

    [Fact]
    public void A_loop_from_the_assembler_counts_down_and_stops_at_reti()
    {
        var cpu = Harness.Program("""
            ;the sum 1+2+3+4+5 in r0
            	movi r1, 5
            	movi r0, 0
            loop:
            	add r0, r1
            	sub r1, 1
            	bnez r1, loop
            	reti
            """);

        var last = cpu.Run();

        cpu.Regs[0].Should().Be(15);
        cpu.Regs[1].Should().Be(0);
        last.Reason.Should().Be(StopReason.Reti);
        last.Address.Should().Be(Ram + 10);
        cpu.InstructionsRetired.Should().Be(18);
    }

    [Fact]
    public void A_nested_call_follows_the_abi_and_keeps_the_link_register()
    {
        //square() calls scale2(), so it has to save R14 first (doc/RiscA.MD lines 154-160)
        var cpu = Harness.Program("""
            	movi r15, 1
            	movl r15, 0x10
            	movl r15, 0        ;r15 = 0x0001_1000, the top of the stack inside the ram
            	movi r0, 6
            	call square
            	reti
            square:
            	sub r15, 4
            	stw [r15+0], r14   ;PUSH link
            	mul r0, r0
            	call scale2
            	ldw r14, [r15+0]   ;POP link
            	add r15, 4
            	ret
            	nop                ;scale2 has to start on a 4 byte boundary for CALL
            scale2:
            	movi r1, 2
            	mul r0, r1
            	ret
            """, ramSize: 8192);

        var last = cpu.Run();

        cpu.Regs[0].Should().Be(72);          //2 * 6 * 6
        cpu.Regs[15].Should().Be(0x0001_1000); //the stack pointer is balanced
        last.Reason.Should().Be(StopReason.Reti);
        cpu.InstructionsRetired.Should().Be(16);
        cpu.Space.ReadWord(0x0001_0FFC).Should().Be(Ram + 10); //the saved link is still in the stack frame
    }

    [Fact]
    public void A_word_constant_after_the_program_is_read_by_ldi()
    {
        //the constant pool idiom of doc/RiscA.MD line 162: constants live behind the last ret or reti
        var cpu = Harness.Program("""
            	ldi r0, [answer]
            	reti
            answer:
            	dw 1234
            """);

        var last = cpu.Run();

        cpu.Regs[0].Should().Be(1234);
        last.Reason.Should().Be(StopReason.Reti);
        cpu.InstructionsRetired.Should().Be(2);
    }

    [Fact]
    public void A_program_drives_a_device_through_the_named_slots()
    {
        var cpu = Harness.Program("""
            	.equ UART0,     0x0000   ;the device block of uart0
            	.equ RX_DATA,   0        ;slot 0
            	.equ RX_READY,  1        ;slot 1
            	.equ TX_DATA,   2        ;slot 2
            	.equ TX_READY,  3        ;slot 3
            	movi r7, UART0
            	movi r2, RX_READY
            	ldb r3, [r7+0]
            send:
            	ldw r0, [r7+TX_READY]
            	bnez r0, ready
            	jr send                  ;a spin loop the instruction limit would catch
            ready:
            	movi r1, 65         ;the letter A, there are no character literals in the tokenizer
            	stw [r7+TX_DATA], r1
            	reti
            """);

        var uart = new FakeDevice("uart0", 4, [0x42, 1, 0, 1]);
        cpu.Space.AddDevice("uart0", 0, uart);

        var last = cpu.Run();

        cpu.Regs[3].Should().Be(0x42);             //rx_data of the fake device
        cpu.Regs[1].Should().Be(65);
        uart.Written.Should().HaveCount(1);
        uart.Written[0].Should().Be((8, 65u)); //slot 2 -> byte offset 8 of block 0
        last.Reason.Should().Be(StopReason.Reti);
    }

    [Fact]
    public void The_image_lands_in_ram_at_the_reset_vector_and_the_listing_matches_the_machine()
    {
        var cpu = Harness.Program("""
            	movi r0, 7
            	add r0, 1
            	reti
            """);

        var trace = new System.Collections.Generic.List<string>();
        cpu.OnRetire = step => trace.Add(step.ToString());

        cpu.Run();

        //the assembler put the program at address 0, the machine loaded it at the reset vector
        //the last step also carries why the machine stopped
        trace.Should().Equal(
            $"0x{Ram:X8} 0702  MOVI R0, 7",
            $"0x{Ram + 2:X8} 0301  ADD  R0, 1",
            $"0x{Ram + 4:X8} 0087  RETI   ; RETI: the program ended (stop convention of the model)");
        cpu.PC.Should().Be(Ram + 6);
    }

    [Fact]
    public void An_assembler_source_without_reti_runs_off_the_image_into_the_zeroed_ram()
    {
        //a program that never stops hits the NOPs of the ram, the instruction limit is the guard
        var cpu = Harness.Program("""
            	movi r0, 1
            """);

        var last = cpu.Run(100);

        last.Reason.Should().Be(StopReason.InstructionLimit);
        cpu.Regs[0].Should().Be(1);
        cpu.InstructionsRetired.Should().Be(100);
    }

    [Fact]
    public void A_far_branch_relaxed_to_an_inverted_branch_plus_jr_still_takes_the_jump()
    {
        //300 NOPs put 'end' past the +-128 instructions of the branch, so pass B inverts the beqz
        //and inserts a jr behind it (doc/assembler-architecture.md, 6.4); r0 is not zero, the
        //inverted branch falls through onto the jr, which skips the poison instruction at r0, 99
        var cpu = Harness.Program("movi r0, 1\nbeqz r0, end\nmovi r0, 99\n"
            + string.Concat(Enumerable.Repeat("nop\n", 300)) + "end: movi r0, 42\nreti\n");

        var last = cpu.Run();

        cpu.Regs[0].Should().Be(42);
        last.Reason.Should().Be(StopReason.Reti);
    }

    [Fact]
    public void A_far_j_relaxes_to_ldi_rN_plus_jmp_rN_and_lands_on_the_target()
    {
        //1100 NOPs put 'far' past the +-1024 instructions of JR: the j expands into LDI r3 from an
        //inline mini pool (the pool slot at the end of the region is out of LDI reach itself) plus
        //JMP r3. Assembled for the load address, so the dw pair carries the real code address (doc 6.1, 6.3)
        var cpu = Harness.Program("j far, r3\n" + string.Concat(Enumerable.Repeat("nop\n", 1100)) + "far: movi r1, 7\nreti\n",
            ramSize: 8192, origin: (int)Harness.Origin);

        var last = cpu.Run();

        cpu.Regs[1].Should().Be(7);
        last.Reason.Should().Be(StopReason.Reti);
    }

    [Fact]
    public void A_far_call_relaxes_to_ldi_r7_plus_call_r7_and_returns_to_the_caller()
    {
        //2100 NOPs put 'work' past the +-1024 words of CALL: the call expands into LDI r7 from an
        //inline mini pool plus CALL r7 (doc 6.4); the link register works as with a near call, the
        //reti right behind the call is the return point
        var cpu = Harness.Program("call work\nreti\n" + string.Concat(Enumerable.Repeat("nop\n", 2100)) + "work: movi r0, 5\nret\n",
            ramSize: 16384, origin: (int)Harness.Origin);

        var last = cpu.Run();

        cpu.Regs[0].Should().Be(5);
        last.Reason.Should().Be(StopReason.Reti);
    }

    [Fact]
    public void La_loads_the_address_of_a_label_from_the_constant_pool_of_its_region()
    {
        //the pool dw holds the absolute address of 'data'; loading it with the same origin the
        //image is placed at makes a register load through r0 read the word constant itself (doc 6.3)
        var cpu = Harness.Program("""
            .func main:
            la r0, data
            ldw r1, [r0+0]
            reti
            nop
            data: dw 1234
            """, origin: (int)Harness.Origin);

        var last = cpu.Run();

        cpu.Regs[1].Should().Be(1234);
        last.Reason.Should().Be(StopReason.Reti);
    }
}
