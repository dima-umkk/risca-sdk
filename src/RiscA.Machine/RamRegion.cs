using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Machine
{
    //Plain memory of the flat address space: a byte array, little-endian words.
    //Unaligned word accesses are allowed here, the LDI constant pool puts 32 bit constants at
    //2 byte (one instruction) alignment, so a constant is not always word aligned (doc/RiscA.MD line 6).
    public sealed class RamRegion(string name, uint bas, int size) : IMappedRegion
    {
        private readonly byte[] _data = new byte[size <= 0 ? throw new ArgumentOutOfRangeException(nameof(size)) : size];

        public string Name { get; } = name;
        public uint Base { get; } = bas;
        public int Size => _data.Length;

        public bool Contains(uint address, int length)
            => address >= Base && address - Base + (uint)length <= (uint)Size;

        public ReadOnlySpan<byte> Bytes => _data;

        private int Index(uint address, int length)
        {
            if (!Contains(address, length))
                throw new BusFault(address, BusFaultKind.Unmapped,
                    $"{length} byte access at 0x{address:X8} is outside {Name} (0x{Base:X8}..0x{Base + (uint)Size - 1:X8})");
            return (int)(address - Base);
        }

        public ushort ReadHalfword(uint address)
        {
            int i = Index(address, 2);
            return (ushort)(_data[i] | (_data[i + 1] << 8));
        }

        public void WriteHalfword(uint address, ushort value)
        {
            int i = Index(address, 2);
            _data[i] = (byte)value;
            _data[i + 1] = (byte)(value >> 8);
        }

        public uint ReadWord(uint address)
        {
            int i = Index(address, 4);
            return (uint)(_data[i] | (_data[i + 1] << 8) | (_data[i + 2] << 16) | (_data[i + 3] << 24));
        }

        public void WriteWord(uint address, uint value)
        {
            int i = Index(address, 4);
            _data[i] = (byte)value;
            _data[i + 1] = (byte)(value >> 8);
            _data[i + 2] = (byte)(value >> 16);
            _data[i + 3] = (byte)(value >> 24);
        }

        public byte ReadByte(uint address) => _data[Index(address, 1)];

        //a single byte lane is exactly the byte strobe 1 << (address & 3) of the hardware bus
        public void WriteByte(uint address, byte value) => _data[Index(address, 1)] = value;
    }
}
