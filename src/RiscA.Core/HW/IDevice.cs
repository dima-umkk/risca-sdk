using System;

namespace RiscA.Core.HW
{
    //A device on the flat bus: it decodes absolute addresses itself and answers only in bytes and words.
    public interface IDevice
    {
        string Name { get; }

        //true when the whole range belongs to this device
        bool Contains(uint address, int length);

        byte ReadByte(uint address);
        void WriteByte(uint address, byte value);

        uint ReadWord(uint address);
        void WriteWord(uint address, uint value);
    }
}
