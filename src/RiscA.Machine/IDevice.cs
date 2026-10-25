using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Machine
{
    //A memory mapped device. Registers are 32 bit words in slots 0..Size/4-1 of the device block
    //(doc/emulator-architecture.md, §4). A device with more than 8 registers uses a select+data pair.
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
