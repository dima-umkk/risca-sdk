using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Core.ISA
{
    public enum ALUFunc : byte
    {
        Mov = 0,
        Add = 1,
        Sub = 2,
        And = 3,
        Or = 4,
        Xor = 5,
        Not = 6,
        Mul = 7,
    }

    public enum ALUImmFunc : byte
    {
        Shl = 0,
        Shr = 1,
        Add = 2,
        Sub = 3,
    }

    public enum RegImmFunc : byte
    {
        MovI = 0,
        MovL = 1,
    }

    public enum LdStFunc : byte
    {
        LD = 0,
        ST = 1,
    }

    public enum LdStBWFunc : byte
    {
        B = 0,
        W = 1,
    }

    public enum BranchFunc : byte
    {
        BEQZ = 0,
        BNEZ = 1,
        BGTZ = 2,
        BLTZ = 3,
    }

    public enum CallJmpRetFunc : byte
    {
        CALL_IMM = 0,
        CALL_REG = 1,
        RET = 2,
        JR = 3,
    }

    public enum OpCode
    {
        ALU_REG_REG = 0,
        ALU_REG_IMM = 1,
        REG_IMM = 2,
        ST_LD = 3,
        BRANCH = 4,
        LDI = 5,
        CALL_JMP_RET = 6,
        INT_RETI = 7,
    }

    //16 bit RiscA instruction
    public readonly struct Instruction(ushort raw)
    {
        //Instructions encoding
        private static readonly (ushort, ushort) MSK_OPCODE =   (0b0000_0000_0000_0111, 0);
        private static readonly (ushort, ushort) MSK_RD =       (0b0000_0000_0111_1000, 3);
        private static readonly (ushort, ushort) MSK_RS =       (0b0000_0111_1000_0000, 7);
        private static readonly (ushort, ushort) MSK_FUNC1 =    (0b0000_0000_1000_0000, 7);
        private static readonly (ushort, ushort) MSK_FUNC2 =    (0b0000_0001_1000_0000, 7);
        private static readonly (ushort, ushort) MSK_FUNC21 =   (0b0000_1000_0000_0000, 11);
        private static readonly (ushort, ushort) MSK_FUNC22 =   (0b0001_0000_0000_0000, 12);
        private static readonly (ushort, ushort) MSK_FUNC3 =    (0b0011_1000_0000_0000, 11);
        private static readonly (ushort, ushort) MSK_IMM3 =     (0b1110_0000_0000_0000, 13);
        private static readonly (ushort, ushort) MSK_IMM7 =     (0b1111_1110_0000_0000, 9);
        private static readonly (ushort, ushort) MSK_IMM8 =     (0b1111_1111_0000_0000, 8);
        private static readonly (ushort, ushort) MSK_IMM9 =     (0b1111_1111_1000_0000, 7);
        private ushort Inv((ushort, ushort) mask) => (ushort)~mask.Item1;
        private ushort Get((ushort, ushort) mask) => (ushort)((_raw & mask.Item1)>>mask.Item2);
        private ushort Apply(int value, (ushort, ushort) mask) => (ushort)((_raw & Inv(mask)) | ((value<<mask.Item2) & mask.Item1));

        private readonly ushort _raw = raw;

        public ushort Raw => _raw;
        public OpCode OpCode => (OpCode)(Get(MSK_OPCODE));
        public int Rd => Get(MSK_RD);
        public int Rs => Get(MSK_RS);
        public int Func1 => Get(MSK_FUNC1);
        public int Func2 => Get(MSK_FUNC2);
        public int Func21 => Get(MSK_FUNC21);
        public int Func22 => Get(MSK_FUNC22);
        public int Func3 => Get(MSK_FUNC3);
        public int Imm3 => Get(MSK_IMM3);
        public int Imm7 => Get(MSK_IMM7);
        public int Imm8 => Get(MSK_IMM8);
        public int Imm9 => Get(MSK_IMM9);
        public Instruction withOpCode(OpCode opcode) => new(Apply((int)opcode, MSK_OPCODE));
        public Instruction withRd(int rd) => new(Apply(rd, MSK_RD));
        public Instruction withRs(int rs) => new(Apply(rs, MSK_RS));
        public Instruction withFunc1(int func1) => new(Apply(func1, MSK_FUNC1));
        public Instruction withFunc2(int func2) => new(Apply(func2, MSK_FUNC2));
        public Instruction withFunc21(int func21) => new(Apply(func21, MSK_FUNC21));
        public Instruction withFunc22(int func22) => new(Apply(func22, MSK_FUNC22));
        public Instruction withFunc3(int func3) => new(Apply(func3, MSK_FUNC3));
        public Instruction withImm3(int imm3) => new(Apply(imm3, MSK_IMM3));
        public Instruction withImm7(int imm7) => new(Apply(imm7, MSK_IMM7));
        public Instruction withImm8(int imm8) => new(Apply(imm8, MSK_IMM8));
        public Instruction withImm9(int imm9) => new(Apply(imm9, MSK_IMM9));

        public void CheckImmLimits(int imm)
        {
            switch (OpCode)
            {
                case OpCode.ALU_REG_IMM:
                    if (imm < 0 || imm > 127)
                        throw new ArgumentOutOfRangeException(nameof(imm), imm, $"Immediate must be in range 0..127 for {OpCode}.");
                    break;

                case OpCode.REG_IMM:
                    if (imm < 0 || imm > 255)
                        throw new ArgumentOutOfRangeException(nameof(imm), imm, $"Immediate must be in range 0..255 for {OpCode}.");
                    break;

                case OpCode.ST_LD:
                    if (imm < 0 || imm > 7)
                        throw new ArgumentOutOfRangeException(nameof(imm), imm, $"Offset must be in range 0..7 for {OpCode}.");
                    break;

                case OpCode.BRANCH:
                    if (imm < -64 || imm > 63)
                        throw new ArgumentOutOfRangeException(nameof(imm), imm, $"Offset must be in range -64..63 for {OpCode}.");
                    break;

                case OpCode.LDI:
                    if (imm < -256 || imm > 255)
                        throw new ArgumentOutOfRangeException(nameof(imm), imm, $"Offset must be in range -256..255 for {OpCode}.");
                    break;

                case OpCode.CALL_JMP_RET:
                    switch ((CallJmpRetFunc)Func2)
                    {
                        case CallJmpRetFunc.CALL_IMM:
                        case CallJmpRetFunc.JR:
                            if (imm < -1024 || imm > 1023)
                                throw new ArgumentOutOfRangeException(nameof(imm), imm, $"Offset must be in range -1024..1023 for {OpCode}.");
                            break;
                        default:
                            return;
                    }
                    break;

                default:
                    return;
            }
        }

        public Instruction SetImm(int imm)
        {
            switch (OpCode)
            {
                case OpCode.ALU_REG_IMM:
                    return this.withImm7(imm);

                case OpCode.REG_IMM:
                    return this.withImm8(imm);

                case OpCode.ST_LD:
                    return this.withImm3(imm);

                case OpCode.BRANCH:
                    return this.withImm7(imm);

                case OpCode.LDI:
                    return this.withImm9(imm);

                case OpCode.CALL_JMP_RET:
                    switch ((CallJmpRetFunc)Func2)
                    {
                        case CallJmpRetFunc.CALL_IMM:
                        case CallJmpRetFunc.JR:
                            return this.withRd(imm & 0b0000_1111).withImm7((imm >> 4) & 0b0111_1111);
                        default:
                            return this;
                    }

                default:
                    return this;
            }
        }

        public override string ToString() 
        {
            return OpCode switch
            {
                OpCode.ALU_REG_REG => $"{(ALUFunc)Func3} R{Rd}, R{Rs} (0x{_raw:X4})",
                OpCode.ALU_REG_IMM => $"{(ALUImmFunc)Func2} R{Rd}, {Imm7} (0x{_raw:X4})",
                OpCode.REG_IMM => $"{(RegImmFunc)Func1} R{Rd}, {Imm8} (0x{_raw:X4})",
                OpCode.ST_LD => $"{(LdStFunc)Func21}{(LdStBWFunc)Func22} R{Rd}, [R{Rs}+{Imm3}] (0x{_raw:X4})",
                OpCode.BRANCH => $"{(BranchFunc)Func2} R{Rd}, {Imm7} (0x{_raw:X4})",
                OpCode.LDI => $"LDI R{Rd}, [{Imm9}] (0x{_raw:X4})",
                OpCode.CALL_JMP_RET => (CallJmpRetFunc)Func2 switch
                {
                    CallJmpRetFunc.CALL_IMM => $"CALL {(Imm7<<4) | Rd} (0x{_raw:X4})",
                    CallJmpRetFunc.CALL_REG => $"CALL R{Rd} (0x{_raw:X4})",
                    CallJmpRetFunc.RET when Rd == 14 => $"RET (0x{_raw:X4})",
                    CallJmpRetFunc.RET when Rd != 14 => $"JMP R{Rd} (0x{_raw:X4})",
                    CallJmpRetFunc.JR => $"JR {(Imm7 << 4) | Rd} (0x{_raw:X4})",
                    _ => throw new NotImplementedException(),
                },
                OpCode.INT_RETI => Func2 switch
                {
                    0 => $"INT R{Rd} (0x{_raw:X4})",
                    1 => $"RETI (0x{_raw:X4})",
                    2 => $"MOV R{Rd}, EPC (0x{_raw:X4})",
                    3 => $"MOV EPC, R{Rd} (0x{_raw:X4})",
                    _ => throw new NotImplementedException(),
                },
                _ => throw new NotImplementedException(),
            };
        }
    }
}
