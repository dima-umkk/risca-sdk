using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Machine
{
    //Why the machine stopped.
    public enum StopReason
    {
        //the instruction executed and the machine keeps running
        Running,
        //RETI is the program end convention of the model (doc/emulator-architecture.md, §6)
        Reti,
        //INT: interrupts and syscalls are not implemented yet, the machine stops with a diagnostic
        IntNotImplemented,
        //an encoding the spec leaves unused, for example ALU func4 = 12
        IllegalInstruction,
        //the bus could not serve an access, the message says which one
        BusFault,
        //Run() reached its instruction limit, a runaway loop stops here
        InstructionLimit,
        //the machine is stopped, Step() does nothing until Reset()
        Halted,
    }

    //One step of the machine: the instruction at Address and what came out of it.
    public sealed record StepResult(uint Address, Instruction Instruction, StopReason Reason, string? Message = null)
    {
        public bool IsRunning => Reason == StopReason.Running;

        //the listing form the debugger and rsim -v print
        public override string ToString()
            => $"0x{Address:X8} {Instruction.Raw:X4}  {Instruction}{(Message is string m ? $"   ; {m}" : "")}";
    }

    //The CPU: an instruction accurate (retire) model of the Risc-A core (doc/emulator-architecture.md, §2).
    //Everything the spec defines as a side effect of an instruction is here, pipeline and timing are not.
    public sealed class Machine
    {
        private readonly AddressSpace _space;

        public AddressSpace Space => _space;
        public Registers Regs { get; } = new Registers();
        //the program counter, the debugger reads and writes it
        public uint PC { get; set; }
        public ulong InstructionsRetired { get; private set; }
        public bool Halted { get; private set; }
        public StepResult Last { get; private set; }
        //trace hook: rsim -v and the debugger print every step through it
        public Action<StepResult>? OnRetire { get; set; }

        public Machine(AddressSpace space)
        {
            _space = space;
            PC = space.ResetVector;
            Last = new StepResult(PC, new Instruction(0), StopReason.Running);
        }

        public void Reset()
        {
            Regs.Reset();
            PC = _space.ResetVector;
            InstructionsRetired = 0;
            Halted = false;
            Last = new StepResult(PC, new Instruction(0), StopReason.Running);
        }

        //Run until the program ends, the bus faults or the limit is reached.
        public StepResult Run(ulong instructionLimit = ulong.MaxValue)
        {
            var last = Last;
            for (ulong done = 0; !Halted && done < instructionLimit; done++)
                last = Step();
            if (!Halted)
                last = last with { Reason = StopReason.InstructionLimit, Message = $"stopped after {instructionLimit} instructions" };
            return last;
        }

        //Execute exactly one instruction. Stepping a halted machine does nothing.
        public StepResult Step()
        {
            if (Halted)
                return Last with { Reason = StopReason.Halted, Message = "the machine is halted, Step() does nothing until Reset()" };

            uint address = PC;
            StepResult result;

            if ((address & 1) != 0)
            {
                //a jump to an odd address: the 16 bit instruction would straddle two 32 bit bus words
                return Stop(new StepResult(address, new Instruction(0), StopReason.BusFault,
                    $"the instruction fetch at 0x{address:X8} is not aligned to 2 bytes"));
            }

            ushort raw;
            try
            {
                raw = _space.ReadHalfword(address);
            }
            catch (BusFault f)
            {
                return Stop(new StepResult(address, new Instruction(0), StopReason.BusFault, $"fetch: {f.Message}"));
            }

            var inst = new Instruction(raw);
            PC = address + 2;

            try
            {
                result = Execute(inst, address);
            }
            catch (BusFault f)
            {
                result = new StepResult(address, inst, StopReason.BusFault, f.Message);
            }

            //a fault does not retire, does not advance the bus and leaves the PC on the instruction
            //that faulted, so the debugger can show it
            if (result.Reason is StopReason.BusFault or StopReason.IllegalInstruction)
            {
                PC = address;
                Halted = true;
                Last = result;
                return result;
            }

            //instruction accurate: the devices advance once per retired instruction
            InstructionsRetired++;
            _space.TickDevices();
            Halted = result.Reason != StopReason.Running;

            Last = result;
            OnRetire?.Invoke(result);
            return result;
        }

        //stop without retiring the instruction that could not be executed
        private StepResult Stop(StepResult result)
        {
            Halted = true;
            Last = result;
            return result;
        }

        //address is the address of the instruction itself, which is what the spec formulas and
        //Instruction.CalcRef use as the base of BRANCH, LDI, CALL and JR offsets
        private StepResult Execute(Instruction inst, uint address)
        {
            switch (inst.OpCode)
            {
                case OpCode.ALU_REG_REG:
                {
                    if (inst.Func4 > (int)ALUFunc.SLTU)
                        return new StepResult(address, inst, StopReason.IllegalInstruction, $"ALU func4 {inst.Func4} is not used by the spec");
                    uint rd = Regs[inst.Rd];
                    uint rs = Regs[inst.Rs];
                    //two operand semantics: the destination is also the left operand (doc/RiscA.MD lines 37-46)
                    Regs[inst.Rd] = (ALUFunc)inst.Func4 switch
                    {
                        ALUFunc.MOV => rs,
                        ALUFunc.ADD => unchecked(rd + rs),
                        ALUFunc.SUB => unchecked(rd - rs),
                        ALUFunc.AND => rd & rs,
                        ALUFunc.OR => rd | rs,
                        ALUFunc.XOR => rd ^ rs,
                        ALUFunc.NOT => ~rs,
                        ALUFunc.MUL => unchecked(rd * rs),
                        ALUFunc.SLT => (int)rd < (int)rs ? 1u : 0u,
                        _ => rd < rs ? 1u : 0u,      //SLTU
                    };
                    break;
                }

                case OpCode.ALU_REG_IMM:
                {
                    uint rd = Regs[inst.Rd];
                    //the shift amount is taken from Imm(4) only (doc/RiscA.MD lines 59-60), the assembler
                    //rejects larger values, a hand made image with one inside behaves like the hardware would
                    Regs[inst.Rd] = (ALUImmFunc)inst.Func2 switch
                    {
                        ALUImmFunc.SHL => rd << (inst.Imm7 & 0xF),
                        ALUImmFunc.SHR => rd >> (inst.Imm7 & 0xF),
                        ALUImmFunc.ADD => unchecked(rd + (uint)inst.Imm7),
                        _ => unchecked(rd - (uint)inst.Imm7),   //SUB
                    };
                    break;
                }

                case OpCode.REG_IMM:
                {
                    //MOVI/MOVL build a constant of any size in a sequence (doc/RiscA.MD lines 73-76)
                    Regs[inst.Rd] = (RegImmFunc)inst.Func1 switch
                    {
                        RegImmFunc.MOVI => (uint)inst.Imm8,
                        _ => (Regs[inst.Rd] << 8) | (uint)inst.Imm8,  //MOVL
                    };
                    break;
                }

                case OpCode.ST_LD:
                {
                    bool store = inst.Func21 == (int)LdStFunc.ST;
                    bool word = inst.Func22 == (int)LdStBWFunc.W;
                    //word access scales the offset by 4, byte access uses it as is (doc/RiscA.MD line 85)
                    uint addr = unchecked(Regs[inst.Rs] + (uint)(word ? inst.Imm3 << 2 : inst.Imm3));
                    if (store)
                    {
                        if (word)
                            _space.WriteWord(addr, Regs[inst.Rd]);
                        else
                            _space.WriteByte(addr, (byte)(Regs[inst.Rd] & 0xFF)); //STB writes the low byte
                    }
                    else
                    {
                        //LDB is zero extended (doc/README.md, opcode 3 table)
                        Regs[inst.Rd] = word ? _space.ReadWord(addr) : _space.ReadByte(addr);
                    }
                    break;
                }

                case OpCode.BRANCH:
                {
                    //the offset is relative to the branch itself and is counted in instructions
                    if ((BranchFunc)inst.Func1 == BranchFunc.BEQZ ? Regs[inst.Rd] == 0 : Regs[inst.Rd] != 0)
                        PC = (uint)inst.CalcRef((int)address);
                    break;
                }

                case OpCode.LDI:
                {
                    //a 32 bit constant from the same address space, PC relative in instructions
                    Regs[inst.Rd] = _space.ReadWord((uint)inst.CalcRef((int)address));
                    break;
                }

                case OpCode.CALL_JMP_RET:
                {
                    switch ((CallJmpRetFunc)inst.Func2)
                    {
                        case CallJmpRetFunc.CALL_IMM:
                            Regs[Registers.LinkRegister] = address + 2;
                            PC = (uint)inst.CalcRef((int)address);
                            break;
                        case CallJmpRetFunc.CALL_REG:
                            Regs[Registers.LinkRegister] = address + 2;
                            PC = Regs[inst.Rd];
                            break;
                        case CallJmpRetFunc.RET:
                            //RET (R14) and JMP Rd are the same encoding (doc/RiscA.MD line 126)
                            PC = Regs[inst.Rd];
                            break;
                        default:
                            PC = (uint)inst.CalcRef((int)address);  //JR, counted in instructions
                            break;
                    }
                    break;
                }

                case OpCode.INT_RETI:
                {
                    switch (inst.Func2)
                    {
                        case 0:
                            return new StepResult(address, inst, StopReason.IntNotImplemented,
                                $"INT R{inst.Rd}: interrupts and syscalls are not implemented in this model (doc/emulator-architecture.md, §6). The program reaches INT on purpose or by accident; add a RETI where the program should end.");
                        case 1:
                            return new StepResult(address, inst, StopReason.Reti, "RETI: the program ended (stop convention of the model)");
                        case 2:
                            Regs[inst.Rd] = Regs.EPC;   //MOV Rd, EPC
                            break;
                        default:
                            Regs.EPC = Regs[inst.Rd];   //MOV EPC, Rd
                            break;
                    }
                    break;
                }

                default:
                    return new StepResult(address, inst, StopReason.IllegalInstruction, $"opcode {inst.OpCode} is not defined by the spec");
            }

            return new StepResult(address, inst, StopReason.Running);
        }
    }
}
