using RiscA.Core.ISA;
using System;

namespace RiscA.Core.HW
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

        //the listing form the debugger and rasim -v print
        public override string ToString()
            => $"0x{Address:X8} {Instruction.Raw:X4}  {Instruction}{(Message is string m ? $"   ; {m}" : "")}";
    }

    //An instruction accurate (retire) model of the Risc-A core: spec side effects yes, pipeline and timing no.
    public sealed class CPU
    {
        //the ABI of doc/RiscA.MD lines 154-159
        public const int ResultRegister = 0;
        public const int LinkRegister = 14;
        public const int StackPointer = 15;

        public Bus Bus { get; }
        public uint PC { get; set; }
        public ulong InstructionsRetired { get; private set; }
        public bool Halted { get; private set; }
        public StepResult Last { get; private set; }
        //trace hook: rasim -v and the debugger print every step through it
        public Action<StepResult>? OnRetire { get; set; }

        //R0-R15 plus EPC of opcode 7; interrupts are not modelled, EPC is only reachable through MOV
        private readonly uint[] _regs = new uint[16];
        public uint EPC { get; set; }

        public uint this[int index]
        {
            get => _regs[Valid(index)];
            set => _regs[Valid(index)] = value;
        }

        private static int Valid(int index)
            => index is >= 0 and < 16 ? index : throw new ArgumentOutOfRangeException(nameof(index), $"R{index} is not a register of this CPU");

        public CPU(Bus bus)
        {
            Bus = bus;
            PC = bus.ResetVector;
            Last = new StepResult(PC, new Instruction(0), StopReason.Running);
        }

        public void Reset()
        {
            Array.Clear(_regs);
            EPC = 0;
            PC = Bus.ResetVector;
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
                raw = Bus.ReadHalfword(address);
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

            //a fault does not retire and leaves the PC on the faulting instruction for the debugger
            if (result.Reason is StopReason.BusFault or StopReason.IllegalInstruction)
            {
                PC = address;
                Halted = true;
                Last = result;
                return result;
            }

            //instruction accurate: the devices advance when the program looks at them
            InstructionsRetired++;
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

        //CalcRef bases branch/call offsets on the instruction address itself, per the spec
        private StepResult Execute(Instruction inst, uint address)
        {
            switch (inst.OpCode)
            {
                case OpCode.ALU_REG_REG:
                {
                    if (inst.Func4 > (int)ALUFunc.SLTU)
                        return new StepResult(address, inst, StopReason.IllegalInstruction, $"ALU func4 {inst.Func4} is not used by the spec");
                    uint rd = _regs[inst.Rd];
                    uint rs = _regs[inst.Rs];
                    //two operand semantics: the destination is also the left operand (doc/RiscA.MD lines 37-46)
                    _regs[inst.Rd] = (ALUFunc)inst.Func4 switch
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
                    uint rd = _regs[inst.Rd];
                    //only the low 4 bits of Imm7 shift (doc/RiscA.MD lines 59-60), like the hardware
                    _regs[inst.Rd] = (ALUImmFunc)inst.Func2 switch
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
                    _regs[inst.Rd] = (RegImmFunc)inst.Func1 switch
                    {
                        RegImmFunc.MOVI => (uint)inst.Imm8,
                        _ => (_regs[inst.Rd] << 8) | (uint)inst.Imm8,  //MOVL
                    };
                    break;
                }

                case OpCode.ST_LD:
                {
                    bool store = inst.Func21 == (int)LdStFunc.ST;
                    bool word = inst.Func22 == (int)LdStBWFunc.W;
                    //word access scales the offset by 4, byte access uses it as is (doc/RiscA.MD line 85)
                    uint addr = unchecked(_regs[inst.Rs] + (uint)(word ? inst.Imm3 << 2 : inst.Imm3));
                    if (store)
                    {
                        if (word)
                            Bus.WriteWord(addr, _regs[inst.Rd]);
                        else
                            Bus.WriteByte(addr, (byte)(_regs[inst.Rd] & 0xFF)); //STB writes the low byte
                    }
                    else
                    {
                        //LDB is zero extended (doc/README.md, opcode 3 table)
                        _regs[inst.Rd] = word ? Bus.ReadWord(addr) : Bus.ReadByte(addr);
                    }
                    break;
                }

                case OpCode.BRANCH:
                {
                    //the offset is relative to the branch itself and is counted in instructions
                    if ((BranchFunc)inst.Func1 == BranchFunc.BEQZ ? _regs[inst.Rd] == 0 : _regs[inst.Rd] != 0)
                        PC = (uint)inst.CalcRef((int)address);
                    break;
                }

                case OpCode.LDI:
                {
                    //a 32 bit constant from the same address space, PC relative in instructions
                    _regs[inst.Rd] = Bus.ReadWord((uint)inst.CalcRef((int)address));
                    break;
                }

                case OpCode.CALL_JMP_RET:
                {
                    switch ((CallJmpRetFunc)inst.Func2)
                    {
                        case CallJmpRetFunc.CALL_IMM:
                            _regs[LinkRegister] = address + 2;
                            PC = (uint)inst.CalcRef((int)address);
                            break;
                        case CallJmpRetFunc.CALL_REG:
                            _regs[LinkRegister] = address + 2;
                            PC = _regs[inst.Rd];
                            break;
                        case CallJmpRetFunc.RET:
                            //RET (R14) and JMP Rd are the same encoding (doc/RiscA.MD line 126)
                            PC = _regs[inst.Rd];
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
                            _regs[inst.Rd] = EPC;   //MOV Rd, EPC
                            break;
                        default:
                            EPC = _regs[inst.Rd];   //MOV EPC, Rd
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

