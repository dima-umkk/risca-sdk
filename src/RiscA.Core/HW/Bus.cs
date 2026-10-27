using System;
using System.Collections.Generic;
using System.Linq;

namespace RiscA.Core.HW
{
    //The flat address space of the model: fetch and loads/stores share one map, self modifying code works.
    public sealed class Bus
    {
        //How the model machine is wired (doc/emulator-architecture.md, §4); the devices themselves are added to the Bus.
        public sealed class Options
        {
            //null means "the start of the first ram on the bus"
            public uint? ResetVector { get; init; }

            //instead of halting, an unmapped read returns 0 and an unmapped write is dropped
            public bool Permissive { get; init; }
        }

        //Why an access could not be completed on the flat bus.
        public enum FaultKind
        {
            //no device answers at this address
            Unmapped,
            //a 32 bit access to a device register that is not aligned to 4 bytes
            Unaligned,
            //a write to a read only device
            ReadOnly,
        }

        public class Fault(uint address, FaultKind kind, string message) : Exception(message)
        {
            public uint Address { get; } = address;
            public FaultKind Kind { get; } = kind;
        }

        private readonly List<IDevice> _devices = [];
        private readonly Options _options;

        public IReadOnlyList<IDevice> Devices => _devices;

        //the cpu starts at the first ram unless asked otherwise
        public uint ResetVector => _options.ResetVector
            ?? _devices.OfType<Ram>().FirstOrDefault()?.Base
            ?? throw new InvalidOperationException("the bus has no ram and no Bus.Options.ResetVector, the cpu would have nowhere to start");

        public Bus(Options? options = null)
        {
            _options = options ?? new Options();
        }

        //everything on the bus is a device: ram and rom included
        public Bus AddDevice(IDevice device)
        {
            _devices.Add(device);
            return this;
        }

        private IDevice? Find(uint address, int length)
        {
            foreach (var device in _devices)
                if (device.Contains(address, length))
                    return device;
            return null;
        }

        private IDevice? Resolve(uint address, int length, bool write)
        {
            var device = Find(address, length);
            if (device is not null)
                return device;
            if (_options.Permissive)
                return null;
            throw new Fault(address, FaultKind.Unmapped,
                $"unmapped {length} byte {(write ? "write" : "read")} at 0x{address:X8}, no device answers there");
        }

        //a load must fit in one mapped range so nothing is written half way
        public void CheckRange(uint address, int length) => _ = Resolve(address, length, false)
            ?? throw new Fault(address, FaultKind.Unmapped, $"cannot load {length} bytes at 0x{address:X8}, the range is not mapped");

        public string RegionName(uint address) => Find(address, 1)?.Name ?? "unmapped";

        //halfwords are composed from bytes, the devices only answer in bytes and words
        public ushort ReadHalfword(uint address)
        {
            var device = Resolve(address, 2, false);
            return device is null ? (ushort)0 : (ushort)(device.ReadByte(address) | (device.ReadByte(address + 1) << 8));
        }

        public void WriteHalfword(uint address, ushort value)
        {
            var device = Resolve(address, 2, true);
            if (device is null)
                return;
            device.WriteByte(address, (byte)value);
            device.WriteByte(address + 1, (byte)(value >> 8));
        }

        public uint ReadWord(uint address) => Resolve(address, 4, false) is IDevice d ? d.ReadWord(address) : 0u;

        public void WriteWord(uint address, uint value)
        {
            if (Resolve(address, 4, true) is IDevice d)
                d.WriteWord(address, value);
        }

        public byte ReadByte(uint address) => Resolve(address, 1, false) is IDevice d ? d.ReadByte(address) : (byte)0;

        public void WriteByte(uint address, byte value)
        {
            if (Resolve(address, 1, true) is IDevice d)
                d.WriteByte(address, value);
        }
    }
}
