using RiscA.Core.Asm;
using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.IO;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

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

//A device that holds instructions: proves that the flat address bus really fetches code from anywhere.
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

//A console for tests: the input is scripted, the output is collected in memory.
internal sealed class ScriptConsole : IConsole
{
    private readonly Queue<int> _input = new Queue<int>();

    //the bytes the program sent out, in order
    public List<int> Output { get; } = [];
    public bool InputEnded { get; set; }

    //the output as text
    public string Text => new string(Output.Select(c => (char)c).ToArray());

    public ScriptConsole Feed(string text)
    {
        foreach (char c in text)
            _input.Enqueue(c);
        return this;
    }

    //no more input is coming: ReadChar() will keep saying "nothing to read"
    public ScriptConsole End()
    {
        InputEnded = true;
        return this;
    }

    public int ReadChar() => _input.Count > 0 ? _input.Dequeue() : -1;

    public void WriteChar(int value) => Output.Add((byte)value);

    public void Flush()
    {
    }
}

internal static class Harness
{
    //the default load and start address: the start of the ram behind the device window (doc/emulator-architecture.md, §4)
    public const uint Origin = BusOptions.DeviceWindowEnd;

    public static Bus NewBus(int ramSize = 4096, bool permissive = false)
        => new(new BusOptions { RamSize = ramSize, Permissive = permissive });

    //a machine with the code loaded where the CPU starts, which is the reset vector of the model
    public static CPU Cpu(params Instruction[] code)
    {
        var cpu = new CPU(NewBus());
        ProgramImage.Load(cpu.Bus, cpu.PC, code);
        return cpu;
    }

    //preset registers before the run, negative values are written as the 32 bit pattern they stand for
    public static CPU With(this CPU cpu, params (int reg, int value)[] preset)
    {
        foreach (var (reg, value) in preset)
            cpu.Regs[reg] = unchecked((uint)value);
        return cpu;
    }

    //assemble with the real assembler into a flat image, assembled for the origin the image is loaded at
    public static byte[] Assemble(string source, int origin = 0)
    {
        var dir = Path.Combine(Path.GetTempPath(), "risca-machine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.rasm");
            File.WriteAllText(file, source);
            return AssembleFile(file, origin);
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
    public static CPU Program(string source, int ramSize = 4096, int origin = 0)
    {
        var cpu = new CPU(NewBus(ramSize));
        ProgramImage.Load(cpu.Bus, cpu.PC, Assemble(source, origin));
        return cpu;
    }

    //like Assemble() but for an existing file, so the repository samples keep their .include paths
    public static byte[] AssembleFile(string path, int origin = 0)
    {
        var asm = new Assembler(origin);
        asm.Compile(path);

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

    //the repository root, found by walking up from the test binaries
    public static string RepoRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "RiscASDK.slnx")))
                    return dir.FullName;
            throw new DirectoryNotFoundException($"no RiscASDK.slnx above {AppContext.BaseDirectory}");
        }
    }

    //a console uart in device block 0, the address uart_getc and uart_putc of samples/lib/uart.rasm use
    public static UartDevice AddUart(this CPU cpu, ScriptConsole console, int txCyclesPerByte = 0)
    {
        var uart = new UartDevice(console, txCyclesPerByte);
        cpu.Bus.AddDevice(UartDevice.DeviceName, UartDevice.Block, uart);
        return uart;
    }

    //a sample of the repository, assembled as is and started at the reset vector with the console uart
    public static (CPU cpu, UartDevice uart) Sample(string file, ScriptConsole console, int ramSize = 8192, int txCyclesPerByte = 0)
    {
        var cpu = new CPU(NewBus(ramSize));
        ProgramImage.Load(cpu.Bus, cpu.PC, AssembleFile(Path.Combine(RepoRoot, "samples", file)));
        var uart = cpu.AddUart(console, txCyclesPerByte);
        return (cpu, uart);
    }
}
