using System;
using System.Text;

namespace RiscA.Machine
{
    //R0-R15 of doc/RiscA.MD line 8 plus the EPC register of opcode 7 (doc/RiscA.MD line 143).
    //Interrupts are not modelled yet (doc/emulator-architecture.md, §6), so EPC is just a register
    //that MOV Rd, EPC and MOV EPC, Rd can read and write, nothing else uses it.
    public sealed class Registers
    {
        public const int Count = 16;

        //the ABI of doc/RiscA.MD lines 154-159
        public const int ResultRegister = 0;
        public const int LinkRegister = 14;
        public const int StackPointer = 15;

        private readonly uint[] _r = new uint[Count];

        public uint EPC { get; set; }

        public uint this[int index]
        {
            get => _r[Valid(index)];
            set => _r[Valid(index)] = value;
        }

        private static int Valid(int index)
            => index is >= 0 and < Count ? index : throw new ArgumentOutOfRangeException(nameof(index), $"R{index} is not a register of this CPU");

        public void Reset()
        {
            Array.Clear(_r);
            EPC = 0;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                if (i != 0)
                    sb.Append(i % 4 == 0 ? "\n" : " ");
                sb.Append($"R{i,-2} = {_r[i]:X8}");
            }
            return sb.ToString();
        }
    }
}
