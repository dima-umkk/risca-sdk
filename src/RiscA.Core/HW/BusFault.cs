using System;

namespace RiscA.Core.HW
{
    //Why an access could not be completed on the flat bus.
    public enum BusFaultKind
    {
        //no device answers at this address
        Unmapped,
        //a 32 bit access to a device register that is not aligned to 4 bytes
        Unaligned,
        //a write to a read only device
        ReadOnly,
    }

    public class BusFault(uint address, BusFaultKind kind, string message) : Exception(message)
    {
        public uint Address { get; } = address;
        public BusFaultKind Kind { get; } = kind;
    }
}
