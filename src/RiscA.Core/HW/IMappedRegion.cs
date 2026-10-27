using System;

namespace RiscA.Core.HW
{
    //A decoded block of the flat map, RAM or a device; byte addresses, little-endian 32 bit words.
    public interface IMappedRegion
    {
        string Name { get; }
        uint Base { get; }
        //size of the decoded block in bytes
        int Size { get; }

        bool Contains(uint address, int length);

        //16 bit access, this is what the instruction fetch path uses
        ushort ReadHalfword(uint address);
        void WriteHalfword(uint address, ushort value);

        //32 bit access, this is what LDW/STW and LDI use
        uint ReadWord(uint address);
        void WriteWord(uint address, uint value);

        //8 bit access, this is what LDB/STB use
        byte ReadByte(uint address);
        void WriteByte(uint address, byte value);
    }
}
