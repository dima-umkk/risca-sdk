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
        private ushort Apply(ushort value, (ushort, ushort) mask) => (ushort)((_raw & Inv(mask)) | ((value<<mask.Item2) & mask.Item1));

        private readonly ushort _raw = raw;

        public ushort Raw => _raw;
        public OpCode OpCode => (OpCode)(Get(MSK_OPCODE));
        public byte Rd => (byte)(Get(MSK_RD));
        public byte Rs => (byte)(Get(MSK_RS));
        public byte Func1 => (byte)(Get(MSK_FUNC1));
        public byte Func2 => (byte)(Get(MSK_FUNC2));
        public byte Func21 => (byte)(Get(MSK_FUNC21));
        public byte Func22 => (byte)(Get(MSK_FUNC22));
        public byte Func3 => (byte)(Get(MSK_FUNC3));
        public byte Imm3 => (byte)(Get(MSK_IMM3));
        public byte Imm7 => (byte)(Get(MSK_IMM7));
        public byte Imm8 => (byte)(Get(MSK_IMM8));
        public byte Imm9 => (byte)(Get(MSK_IMM9));
        public Instruction withOpCode(ushort opcode) => new(Apply(opcode, MSK_OPCODE));
        public Instruction withRd(byte rd) => new(Apply(rd, MSK_RD));
        public Instruction withRs(byte rs) => new(Apply(rs, MSK_RS));
        public Instruction withFunc1(byte func1) => new(Apply(func1, MSK_FUNC1));
        public Instruction withFunc2(byte func2) => new(Apply(func2, MSK_FUNC2));
        public Instruction withFunc21(byte func21) => new(Apply(func21, MSK_FUNC21));
        public Instruction withFunc22(byte func22) => new(Apply(func22, MSK_FUNC22));
        public Instruction withFunc3(byte func3) => new(Apply(func3, MSK_FUNC3));
        public Instruction withImm3(byte imm3) => new(Apply(imm3, MSK_IMM3));
        public Instruction withImm7(byte imm7) => new(Apply(imm7, MSK_IMM7));
        public Instruction withImm8(byte imm8) => new(Apply(imm8, MSK_IMM8));
        public Instruction withImm9(byte imm9) => new(Apply(imm9, MSK_IMM9));

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
