using RiscA.Core.Asm;
using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.IO;

namespace RiscA.Machine.Tests;

//A stand in device that records every access the program makes to it.
internal class FakeDevice : IDevice
{
    public FakeDevice(string name = "fake", int registerCount = 4, uint[]? initial = null)
    {
        Name = name;
        Slots = new uint[registerCount];
        Size = registerCount * 4;
        if (initial is uint[] seed)
            Array.Copy(seed, Slots, Math.Min(seed.Length, Slots.Length));
    }

    public string Name { get; }
    public int Size { get; }
    //the current value of every register of the device
    public uint[] Slots { get; }
    //what was written, in the order it was written: (byte offset inside the block, value)
    public List<(int Offset, uint Value)> Written { get; } = [];
    public int Reads { get; private set; }
    public int Writes { get; private set; }
    public int Ticks { get; private set; }

    public uint ReadWord(int offset)
    {
        Reads++;
        return Slots[offset / 4];
    }

    public void WriteWord(int offset, uint value)
    {
        Writes++;
        Written.Add((offset, value));
        Slots[offset / 4] = value;
    }

    public void Tick() => Ticks++;
}

//A device that holds instructions: proves that the flat address space really fetches code from anywhere.
internal sealed class RomDevice : IDevice
{
    private readonly ushort[] _halves;

    public RomDevice(string name, params ushort[] halves)
    {
        Name = name;
        _halves = halves;
        //the device answers on whole 32 bit registers, so its size is the number of halfwords rounded up
        Size = ((halves.Length + 1) / 2) * 4;
    }

    public string Name { get; }
    public int Size { get; }

    public uint ReadWord(int offset)
        => (uint)(_halves[offset / 2] | (_halves.Length > offset / 2 + 1 ? _halves[offset / 2 + 1] << 16 : 0));

    public void WriteWord(int offset, uint value)
        => throw new InvalidOperationException("a rom device does not accept writes");

    public void Tick()
    {
    }
}

internal static class Harness
{
    //the default place a program is loaded and started at: the start of the ram, right behind the
    //device window (doc/emulator-architecture.md, §4)
    public const uint Origin = MachineOptions.DeviceWindowEnd;

    public static AddressSpace Space(int ramSize = 4096, bool permissive = false)
        => new(new MachineOptions { RamSize = ramSize, Permissive = permissive });

    //a machine with the code loaded where the CPU starts, which is the reset vector of the model
    public static Machine Cpu(params Instruction[] code)
    {
        var cpu = new Machine(Space());
        ProgramImage.Load(cpu.Space, cpu.PC, code);
        return cpu;
    }

    //preset registers before the run, negative values are written as the 32 bit pattern they stand for
    public static Machine With(this Machine cpu, params (int reg, int value)[] preset)
    {
        foreach (var (reg, value) in preset)
            cpu.Regs[reg] = unchecked((uint)value);
        return cpu;
    }

    //assemble a program with the real assembler and return the flat image, like `rasm -o prog.bin` does
    public static byte[] Assemble(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "risca-machine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.rasm");
            File.WriteAllText(file, source);
            var asm = new Assembler(0);
            asm.Compile(file);

            var bytes = new List<byte>();
            foreach (var line in asm.Src)
                if (line.ParsedInstruction is ParsedInstruction parsed)
                    foreach (var inst in parsed.Instructions)
                    {
                        bytes.Add((byte)inst.Raw);
                        bytes.Add((byte)(inst.Raw >> 8));
                    }
            return [.. bytes];
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
                //the temp folder is disposable anyway
            }
        }
    }

    //a whole program assembled from source and loaded at the reset vector, the start of the ram
    public static Machine Program(string source, int ramSize = 4096)
    {
        var cpu = new Machine(Space(ramSize));
        ProgramImage.Load(cpu.Space, cpu.PC, Assemble(source));
        return cpu;
    }
}
