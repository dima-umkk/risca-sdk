using System;
using System.Collections.Generic;

namespace RiscA.Core.HW
{
    //The flat address space of the model: fetch and loads/stores share one map, self modifying code works.
    public sealed class Bus
    {
        private readonly List<DeviceRegion> _devices = [];

        public BusOptions Options { get; }
        public RamRegion Ram { get; }
        public IReadOnlyList<DeviceRegion> Devices => _devices;
        //the cpu starts at the ram start unless asked otherwise: the low window belongs to the devices
        public uint ResetVector => Options.ResetVector ?? Ram.Base;

        public Bus(BusOptions? options = null)
        {
            Options = options ?? new BusOptions();
            if (Options.RamBase < BusOptions.DeviceWindowEnd)
                throw new ArgumentException($"RAM has to start at 0x{BusOptions.DeviceWindowEnd:X8} or higher, the low window belongs to the devices");
            if (Options.DeviceBlockSize <= 0 || Options.DeviceBlockSize % 4 != 0)
                throw new ArgumentException("the device block size has to be a positive multiple of 4");
            Ram = new RamRegion("ram", Options.RamBase, Options.RamSize);
        }

        public Bus AddDevice(string name, int block, IDevice device)
        {
            uint bas = (uint)block * (uint)Options.DeviceBlockSize;
            if (block < 0 || bas + (uint)Options.DeviceBlockSize > BusOptions.DeviceWindowEnd)
                throw new ArgumentException($"device block {block} does not fit into the device window");
            if (device.Size <= 0 || device.Size % 4 != 0 || device.Size > Options.DeviceBlockSize)
                throw new ArgumentException($"device '{name}' has to declare between 4 and {Options.DeviceBlockSize} bytes of registers in multiples of 4");
            foreach (var other in _devices)
            {
                if (bas < other.Base + (uint)other.Size && other.Base < bas + (uint)Options.DeviceBlockSize)
                    throw new ArgumentException($"device '{name}' at block {block} overlaps device '{other.Name}' at 0x{other.Base:X8}");
            }
            _devices.Add(new DeviceRegion(name, bas, Options.DeviceBlockSize, device));
            _devices.Sort((a, b) => a.Base.CompareTo(b.Base));
            return this;
        }

        //the hardware decode rule: addr[31:16] == 0 selects the device bus, everything else is RAM
        private IMappedRegion? Find(uint address, int length)
        {
            if ((address >> 16) == 0)
            {
                foreach (var device in _devices)
                    if (device.Contains(address, length))
                        return device;
            }
            else if (Ram.Contains(address, length))
                return Ram;
            return null;
        }

        private IMappedRegion? Resolve(uint address, int length, bool write)
        {
            var region = Find(address, length);
            if (region is not null)
                return region;
            if (Options.Permissive)
                return null;
            throw new BusFault(address, BusFaultKind.Unmapped,
                $"unmapped {length} byte {(write ? "write" : "read")} at 0x{address:X8} (devices: 0x00000000..0x{BusOptions.DeviceWindowEnd - 1:X8}, ram: 0x{Ram.Base:X8}..0x{Ram.Base + (uint)Ram.Size - 1:X8})");
        }

        //a load must fit in one mapped range so nothing is written half way
        public void CheckRange(uint address, int length) => _ = Resolve(address, length, false)
            ?? throw new BusFault(address, BusFaultKind.Unmapped, $"cannot load {length} bytes at 0x{address:X8}, the range is not mapped");

        public string RegionName(uint address)
        {
            var region = Find(address, 1);
            return region?.Name ?? "unmapped";
        }

        public void TickDevices()
        {
            foreach (var device in _devices)
                device.Device.Tick();
        }

        public ushort ReadHalfword(uint address) => Resolve(address, 2, false) is IMappedRegion r ? r.ReadHalfword(address) : (ushort)0;

        public void WriteHalfword(uint address, ushort value)
        {
            if (Resolve(address, 2, true) is IMappedRegion r)
                r.WriteHalfword(address, value);
        }

        public uint ReadWord(uint address) => Resolve(address, 4, false) is IMappedRegion r ? r.ReadWord(address) : 0u;

        public void WriteWord(uint address, uint value)
        {
            if (Resolve(address, 4, true) is IMappedRegion r)
                r.WriteWord(address, value);
        }

        public byte ReadByte(uint address) => Resolve(address, 1, false) is IMappedRegion r ? r.ReadByte(address) : (byte)0;

        public void WriteByte(uint address, byte value)
        {
            if (Resolve(address, 1, true) is IMappedRegion r)
                r.WriteByte(address, value);
        }
    }
}
