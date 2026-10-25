using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Machine
{
    //A device block of the device window (doc/emulator-architecture.md, §4): the registers of one IDevice.
    //The block is decoded as a whole, but an access beyond Device.Size is a bus fault, which catches
    //a wrong register number in a program (for example STW [r7+8] when UART0 only has 4 registers).
    public sealed class DeviceRegion(string name, uint bas, int size, IDevice device) : IMappedRegion
    {
        public string Name { get; } = name;
        public uint Base { get; } = bas;
        public int Size { get; } = size;
        public IDevice Device { get; } = device;

        public bool Contains(uint address, int length)
            => address >= Base && address - Base + (uint)length <= (uint)Size;

        private int Offset(uint address, int length)
        {
            if (!Contains(address, length))
                throw new BusFault(address, BusFaultKind.Unmapped,
                    $"{length} byte access at 0x{address:X8} is outside device block {Name} (0x{Base:X8}..0x{Base + (uint)Size - 1:X8})");
            int offset = (int)(address - Base);
            if (offset + length > Device.Size)
                throw new BusFault(address, BusFaultKind.DeviceRange,
                    $"device {Device.Name} has {Device.Size} bytes of registers, the access at 0x{address:X8} reaches further");
            return offset;
        }

        //offset of a 32 bit register: aligned to 4 bytes and inside the registers of the device
        private int RegisterOffset(uint address)
        {
            int offset = Offset(address, 4);
            if ((offset & 3) != 0)
                throw new BusFault(address, BusFaultKind.Unaligned,
                    $"a 32 bit device access at 0x{address:X8} is not aligned to 4 bytes");
            return offset & ~3;
        }

        private int HalfwordOffset(uint address)
        {
            int offset = Offset(address, 2);
            if ((offset & 1) != 0)
                throw new BusFault(address, BusFaultKind.Unaligned,
                    $"a 16 bit device access at 0x{address:X8} is not aligned to 2 bytes");
            return offset;
        }

        public uint ReadWord(uint address) => Device.ReadWord(RegisterOffset(address));

        public void WriteWord(uint address, uint value) => Device.WriteWord(RegisterOffset(address), value);

        public ushort ReadHalfword(uint address)
        {
            int offset = HalfwordOffset(address);
            return (ushort)((Device.ReadWord(offset & ~3) >> ((offset & 3) * 8)) & 0xFFFF);
        }

        public void WriteHalfword(uint address, ushort value)
        {
            int offset = HalfwordOffset(address);
            int shift = (offset & 3) * 8;
            uint reg = (Device.ReadWord(offset & ~3) & ~(0xFFFFu << shift)) | ((uint)value << shift);
            Device.WriteWord(offset & ~3, reg);
        }

        public byte ReadByte(uint address)
        {
            int offset = Offset(address, 1);
            return (byte)((Device.ReadWord(offset & ~3) >> ((offset & 3) * 8)) & 0xFF);
        }

        //a byte access to a device register is a read-modify-write of the whole register, so devices
        //with side effects on a read should not be byte addressed (the ISA samples only use LDB/STB in RAM)
        public void WriteByte(uint address, byte value)
        {
            int offset = Offset(address, 1);
            int shift = (offset & 3) * 8;
            uint reg = (Device.ReadWord(offset & ~3) & ~(0xFFu << shift)) | ((uint)value << shift);
            Device.WriteWord(offset & ~3, reg);
        }
    }
}
