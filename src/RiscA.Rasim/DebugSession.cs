using RiscA.Core.HW;
using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RiscA.Rasim
{
    //The interactive prompt of the debugger: step, run, breakpoints, registers, memory.
    //The program talks to the terminal through the uart, the debugger talks to stderr.
    internal static class DebugSession
    {
        private const string Help = """
            debugger commands:
              s [n]         step n retired instructions, default 1
              c             run to the next breakpoint or to the end of the program
              b <addr>      set a breakpoint
              bl            list breakpoints
              u <addr>      remove a breakpoint
              r             print the registers
              m <addr> [n]  hex dump n bytes of memory, default 64
              h             this help
              q             quit
            """;

        public static int Run(CPU cpu, ulong limit)
        {
            Console.Error.WriteLine("rasim debugger, h lists the commands");
            ShowHere(cpu);

            while (true)
            {
                Console.Error.Write("(rasim) ");
                string? line = Console.In.ReadLine();
                if (line is null)
                    return Cli.Exit.Ok;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;

                switch (parts[0])
                {
                    case "q":
                    case "quit":
                        return Cli.Exit.Ok;
                    case "h":
                    case "help":
                        Console.Error.WriteLine(Help);
                        break;
                    case "s":
                        Step(cpu, parts);
                        break;
                    case "c":
                    case "continue":
                        Continue(cpu, limit);
                        break;
                    case "b":
                        Breakpoint(cpu, parts);
                        break;
                    case "u":
                        Unset(cpu, parts);
                        break;
                    case "bl":
                        ListBreakpoints(cpu);
                        break;
                    case "r":
                        Registers(cpu);
                        break;
                    case "m":
                        Memory(cpu, parts);
                        break;
                    default:
                        Cli.Error($"unknown command '{parts[0]}', h lists the commands");
                        break;
                }
            }
        }

        private static void Step(CPU cpu, string[] parts)
        {
            uint count = 1;
            if (parts.Length > 1 && (!Cli.TryNumber(parts[1], out count) || count == 0))
            {
                Cli.Error("s needs a positive count, s [n]");
                return;
            }
            for (uint i = 0; i < count; i++)
            {
                var step = cpu.Step();
                Console.Error.WriteLine(step.ToString());
                if (!step.IsRunning)
                    return;
                //the prompt stops on a breakpoint the step landed on, like c will later do
                if (i + 1 < count && cpu.HasBreakpoint(cpu.PC))
                {
                    Cli.Error($"breakpoint at 0x{cpu.PC:X8}");
                    return;
                }
            }
            ShowHere(cpu);
        }

        //Run() stops when PC reaches a breakpoint without executing the instruction there
        private static void Continue(CPU cpu, ulong limit)
        {
            var last = cpu.Run(limit);
            if (last.Reason == StopReason.InstructionLimit)
            {
                Cli.Error($"stopped after {limit} instructions, the program is probably stuck in a loop");
                ShowHere(cpu);
                return;
            }
            Console.Error.WriteLine(last.Message is string m ? $"{last.Address:X8}: {m}" : last.ToString());
            if (cpu.Halted)
                Cli.Error("the program has ended, q to quit");
            ShowHere(cpu);
        }

        private static void Breakpoint(CPU cpu, string[] parts)
        {
            if (parts.Length < 2 || !Cli.TryNumber(parts[1], out uint address))
            {
                Cli.Error("b needs an address, b 0x10010");
                return;
            }
            bool isNew = !cpu.HasBreakpoint(address);
            cpu.AddBreakpoint(address);
            Cli.Error(isNew ? $"breakpoint at 0x{address:X8}" : $"breakpoint at 0x{address:X8} already set");
        }

        private static void Unset(CPU cpu, string[] parts)
        {
            if (parts.Length < 2 || !Cli.TryNumber(parts[1], out uint address))
            {
                Cli.Error("u needs an address, u 0x10010");
                return;
            }
            Cli.Error(cpu.RemoveBreakpoint(address) ? $"breakpoint at 0x{address:X8} removed" : $"no breakpoint at 0x{address:X8}");
        }

        private static void ListBreakpoints(CPU cpu)
        {
            if (!cpu.Breakpoints.Any())
            {
                Cli.Error("no breakpoints");
                return;
            }
            foreach (uint address in cpu.Breakpoints.OrderBy(a => a))
                Console.Error.WriteLine($"0x{address:X8}");
        }

        private static void Registers(CPU cpu)
        {
            for (int row = 0; row < 4; row++)
                Console.Error.WriteLine(string.Join("  ",
                    Enumerable.Range(row * 4, 4).Select(r => $"r{r,-2}={cpu[r]:X8}")));
            Console.Error.WriteLine($"pc ={cpu.PC:X8}   epc={cpu.EPC:X8}");
        }

        private static void Memory(CPU cpu, string[] parts)
        {
            if (parts.Length < 2 || !Cli.TryNumber(parts[1], out uint address))
            {
                Cli.Error("m needs an address, m 0x10000 [n]");
                return;
            }
            uint length = 64;
            if (parts.Length > 2 && (!Cli.TryNumber(parts[2], out length) || length == 0))
            {
                Cli.Error("m needs a positive length, m 0x10000 [n]");
                return;
            }
            for (uint row = 0; row < length; address += 16, row += 16)
            {
                var bytes = new List<byte>();
                for (uint col = 0; col < 16 && row + col < length; col++)
                    try
                    {
                        bytes.Add(cpu.Bus.ReadByte(address + col));
                    }
                    catch (Bus.Fault)
                    {
                        break;
                    }
                if (bytes.Count == 0)
                {
                    Cli.Error($"0x{address:X8} is unmapped");
                    return;
                }
                string hex = string.Join(" ", bytes.Select(b => b.ToString("X2")));
                string ascii = new string(bytes.Select(b => b is >= 0x20 and <= 0x7E ? (char)b : '.').ToArray());
                Console.Error.WriteLine($"0x{address:X8}  {hex,-47}  |{ascii}|");
            }
        }

        //the instruction the cpu would execute next: disassembly is the debugger's job now
        private static void ShowHere(CPU cpu)
        {
            string text;
            try
            {
                var inst = new Instruction(cpu.Bus.ReadHalfword(cpu.PC));
                text = $"0x{cpu.PC:X8} {inst.Raw:X4}  {inst}{(cpu.HasBreakpoint(cpu.PC) ? "   ; breakpoint" : "")}";
            }
            catch (Bus.Fault)
            {
                text = $"0x{cpu.PC:X8} reads nothing, the fetch is unmapped";
            }
            Console.Error.WriteLine(text);
        }
    }
}
