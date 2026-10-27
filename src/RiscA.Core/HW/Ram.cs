using System;
using System.Buffers.Binary;

namespace RiscA.Core.HW
{
    //Plain memory: little-endian, unaligned words allowed because the dw pool packs 32 bit constants at 2 byte alignment.
    public class Ram : IDevice
    {
        //the ram of the model starts right behind the device window (doc/emulator-architecture.md, §4)
        public const uint DefaultBase = 0x0001_0000;
        public const int DefaultSize = 64 * 1024;

        private readonly byte[] _data;

        public Ram(string name = "ram", uint bas = DefaultBase, int size = DefaultSize)
        {
            Name = name;
            Base = bas;
            _data = new byte[size <= 0 ? throw new ArgumentOutOfRangeException(nameof(size)) : size];
        }

        public string Name { get; }
        public uint Base { get; }
        public int Size => _data.Length;

        public bool Contains(uint address, int length)
            => address >= Base && address - Base + (uint)length <= (uint)Size;

        private int Index(uint address, int length)
        {
            if (!Contains(address, length))
                throw new Bus.Fault(address, Bus.FaultKind.Unmapped,
                    $"{length} byte access at 0x{address:X8} is outside {Name} (0x{Base:X8}..0x{Base + (uint)Size - 1:X8})");
            return (int)(address - Base);
        }

        public byte ReadByte(uint address) => _data[Index(address, 1)];

        public void WriteByte(uint address, byte value) => _data[Index(address, 1)] = value;

        public uint ReadWord(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Index(address, 4)));

        public void WriteWord(uint address, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(Index(address, 4)), value);
    }
}
