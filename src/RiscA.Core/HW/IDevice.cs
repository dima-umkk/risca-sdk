using System;

namespace RiscA.Core.HW
{
    //A memory mapped device: 32 bit word registers in slots 0..Size/4-1 of its block.
    public interface IDevice
    {
        string Name { get; }

        //number of bytes the device decodes, a multiple of 4 and not larger than the block it sits in
        int Size { get; }

        //offset is a byte offset inside the device block, always aligned to 4 bytes
        uint ReadWord(int offset);
        void WriteWord(int offset, uint value);

        //called once per retired instruction, this is where a device does its own work (baud rate, timers)
        void Tick();
    }
}
