using System;

namespace RiscA.Core.HW
{
    //The memory map of the flat address space (doc/emulator-architecture.md, §4).
    public sealed class BusOptions
    {
        //the device window: addr[31:16] == 0, the first place the hardware decode looks at
        public const uint DeviceWindowEnd = 0x0001_0000;

        //one device occupies a block of this size, the block of device N starts at N * DeviceBlockSize
        public int DeviceBlockSize { get; init; } = 0x100;

        //RAM starts right behind the device window and goes up from there
        public uint RamBase { get; init; } = DeviceWindowEnd;
        public int RamSize { get; init; } = 64 * 1024;

        //null means "the start of the ram"; rasim sets it to the origin of the image (the RTL reset vector 0 stays an RTL convention)
        public uint? ResetVector { get; init; }

        //instead of halting, an unmapped read returns 0 and an unmapped write is dropped
        public bool Permissive { get; init; }
    }
}
