using System;
using System.Buffers.Binary;

namespace RiscA.Core.HW
{
    //Plain memory: little-endian, unaligned words allowed because the dw pool packs 32 bit constants at 2 byte alignment.
    public sealed class RamRegion(string name, uint bas, int size) : IMappedRegion
    {
        private readonly byte[] _data = new byte[size <= 0 ? throw new ArgumentOutOfRangeException(nameof(size)) : size];

        public string Name { get; } = name;
        public uint Base { get; } = bas;
        public int Size => _data.Length;

        public bool Contains(uint address, int length)
            => address >= Base && address - Base + (uint)length <= (uint)Size;

        private int Index(uint address, int length)
        {
            if (!Contains(address, length))
                throw new BusFault(address, BusFaultKind.Unmapped,
                    $"{length} byte access at 0x{address:X8} is outside {Name} (0x{Base:X8}..0x{Base + (uint)Size - 1:X8})");
            return (int)(address - Base);
        }

        public ushort ReadHalfword(uint address) => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(Index(address, 2)));

        public void WriteHalfword(uint address, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(Index(address, 2)), value);

        public uint ReadWord(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Index(address, 4)));

        public void WriteWord(uint address, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(Index(address, 4)), value);

        public byte ReadByte(uint address) => _data[Index(address, 1)];

        //a single byte lane is exactly the byte strobe 1 << (address & 3) of the hardware bus
        public void WriteByte(uint address, byte value) => _data[Index(address, 1)] = value;
    }
}
