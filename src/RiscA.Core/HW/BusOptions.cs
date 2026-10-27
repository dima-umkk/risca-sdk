using System;

namespace RiscA.Core.HW
{
    //How the model machine is wired (doc/emulator-architecture.md, §4); the devices themselves are added to the Bus.
    public sealed class BusOptions
    {
        //null means "the start of the first ram on the bus"
        public uint? ResetVector { get; init; }

        //instead of halting, an unmapped read returns 0 and an unmapped write is dropped
        public bool Permissive { get; init; }
    }
}
