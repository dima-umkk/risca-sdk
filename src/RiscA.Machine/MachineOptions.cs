using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Machine
{
    //The memory map of the flat address space (doc/emulator-architecture.md, §4).
    public sealed class MachineOptions
    {
        //the device window: addr[31:16] == 0, the first place the hardware decode looks at
        public const uint DeviceWindowEnd = 0x0001_0000;

        //one device occupies a block of this size, the block of device N starts at N * DeviceBlockSize
        public int DeviceBlockSize { get; init; } = 0x100;

        //RAM starts right behind the device window and goes up from there
        public uint RamBase { get; init; } = DeviceWindowEnd;
        public int RamSize { get; init; } = 64 * 1024;

        //the reset vector of the model. The low window 0x0000_0000..0x0000_FFFF belongs to the devices
        //(doc/emulator-architecture.md, §4), so a program is loaded into RAM and the reset starts there.
        //null means "the start of the ram"; rsim sets it to the origin of the image it loads.
        //The hardware reset vector 0x00000000 of doc/RiscA.MD line 148 stays the convention of the RTL.
        public uint? ResetVector { get; init; }

        //instead of halting, an unmapped read returns 0 and an unmapped write is dropped
        public bool Permissive { get; init; }
    }
}
