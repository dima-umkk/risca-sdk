using RiscA.Core.Asm;
using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using RiscA.Core.HW;
namespace RiscA.Core.Tests.HW;

//A stand in device that records every access the program makes to it.
internal class FakeDevice : IDevice
{
    public FakeDevice(string name = "fake", uint bas = 0x0000_1000, int registerCount = 4, uint[]? initial = null)
    {
        Name = name;
        Base = bas;
        Slots = new uint[registerCount];
        if (initial is uint[] seed)
            Array.Copy(seed, Slots, Math.Min(seed.Length, Slots.Length));
    }

    public string Name { get; }
    public uint Base { get; }
    //the current value of every register of the device
    public uint[] Slots { get; }
    //what was written, in the order it was written: (byte offset inside the device, value)
    public List<(int Offset, uint Value)> Written { get; } = [];
    public int Reads { get; private set; }
    public int Writes { get; private set; }

    public int Size => Slots.Length * 4;

    public bool Contains(uint address, int length)
        => address >= Base && address - Base + (uint)length <= (uint)Size;

    private int Offset(uint address, int length)
    {
        if (!Contains(address, length))
            throw new Bus.Fault(address, Bus.FaultKind.Unmapped,
                $"{length} byte access at 0x{address:X8} is outside the registers of {Name} (0x{Base:X8}..0x{Base + (uint)Size - 1:X8})");
        return (int)(address - Base);
    }

    private int WordOffset(uint address)
    {
        int off = Offset(address, 4);
        if (off % 4 != 0)
            throw new Bus.Fault(address, Bus.FaultKind.Unaligned,
                $"word access at 0x{address:X8} is not aligned to a register of {Name}");
        return off;
    }

    public uint ReadWord(uint address)
    {
        Reads++;
        return Slots[WordOffset(address) / 4];
    }

    public void WriteWord(uint address, uint value)
    {
        int off = WordOffset(address);
        Writes++;
        Written.Add((off, value));
        Slots[off / 4] = value;
    }

    //a byte access is a read modify write of the whole register
    public byte ReadByte(uint address)
    {
        int off = Offset(address, 1);
        return (byte)(ReadWord(Base + (uint)(off & ~3)) >> (8 * (off & 3)));
    }

    public void WriteByte(uint address, byte value)
    {
        int off = Offset(address, 1);
        uint reg = Base + (uint)(off & ~3);
        uint merged = (ReadWord(reg) & ~(0xFFu << (8 * (off & 3)))) | ((uint)value << (8 * (off & 3)));
        Writes++;
        Written.Add(((int)(reg - Base), merged));
        Slots[(reg - Base) / 4] = merged;
    }
}

//A console for tests: the input is a scripted stream, the output is collected in memory.
internal sealed class ScriptConsole
{
    public MemoryStream Input { get; } = new();
    public MemoryStream Output { get; } = new();

    //the output as text
    public string Text => Encoding.UTF8.GetString(Output.ToArray());

    //bytes the program sent out, in order
    public IReadOnlyList<byte> Bytes => Output.ToArray();

    //the scripted input; feed it before the uart is attached, streams are replayed from their start
    public ScriptConsole Feed(string text)
    {
        foreach (byte b in Encoding.UTF8.GetBytes(text))
            Input.WriteByte(b);
        Input.Position = 0;
        return this;
    }
}

internal static class Harness
{
    //the default load and start address: the start of the ram behind the device window (doc/emulator-architecture.md, §4)
    public const uint Origin = Ram.DefaultBase;

    public static Bus NewBus(int ramSize = 4096, bool permissive = false)
        => new Bus(new Bus.Options { Permissive = permissive }).AddDevice(new Ram("ram", Ram.DefaultBase, ramSize));

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
            cpu[reg] = unchecked((uint)value);
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

    //a console uart wired to the streams of a ScriptConsole, the address uart_getc and uart_putc of samples/lib/uart.rasm use
    public static UartDevice AddUart(this CPU cpu, ScriptConsole console)
    {
        var uart = new UartDevice(console.Input, console.Output);
        cpu.Bus.AddDevice(uart);
        return uart;
    }

    //a sample of the repository, assembled for the load address and started at the reset vector with the console uart
    public static (CPU cpu, UartDevice uart) Sample(string file, ScriptConsole console, int ramSize = 8192, int origin = (int)Origin)
    {
        var cpu = new CPU(NewBus(ramSize));
        ProgramImage.Load(cpu.Bus, cpu.PC, AssembleFile(Path.Combine(RepoRoot, "samples", file), origin));
        var uart = cpu.AddUart(console);
        return (cpu, uart);
    }
}
