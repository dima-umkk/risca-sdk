using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Machine
{
    //Why an access could not be completed on the flat bus of doc/emulator-architecture.md.
    public enum BusFaultKind
    {
        //no region covers the access
        Unmapped,
        //a 32 bit access to a device register that is not aligned to 4 bytes
        Unaligned,
        //the access is inside the block of a device but outside its registers
        DeviceRange,
    }

    public class BusFault(uint address, BusFaultKind kind, string message) : Exception(message)
    {
        public uint Address { get; } = address;
        public BusFaultKind Kind { get; } = kind;
    }
}
