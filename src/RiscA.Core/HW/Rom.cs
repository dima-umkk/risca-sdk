using System;
using System.Buffers.Binary;
using System.IO;

namespace RiscA.Core.HW
{
    //Read only memory: it starts from a file like a board rom does, every write is a bus fault.
    public sealed class Rom : IDevice
    {
        private readonly byte[] _data;

        public Rom(string name, uint bas, byte[] image)
        {
            Name = name;
            Base = bas;
            _data = image;
        }

        public Rom(string name, uint bas, string file) : this(name, bas, File.ReadAllBytes(file))
        {
        }

        public string Name { get; }
        public uint Base { get; }
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

        private BusFault Denied(uint address)
            => new(address, BusFaultKind.ReadOnly, $"writing to rom '{Name}' at 0x{address:X8} is not allowed");

        public byte ReadByte(uint address) => _data[Index(address, 1)];

        public void WriteByte(uint address, byte value) => throw Denied(address);

        public uint ReadWord(uint address) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(Index(address, 4)));

        public void WriteWord(uint address, uint value) => throw Denied(address);
    }
}
