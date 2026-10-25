using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Machine
{
    //A block of the single flat address space: RAM or a device (doc/emulator-architecture.md, §3 and §4).
    //Addresses are byte addresses; the bus is 32 bit, so a word is 4 consecutive bytes, little-endian.
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
