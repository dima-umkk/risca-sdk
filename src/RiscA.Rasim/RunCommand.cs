using RiscA.Core.HW;
using System;
using System.IO;

namespace RiscA.Rasim
{

    //rasim run: load an image, attach the console uart to block 0 and run until stop, fault, limit or hopeless input wait.
    internal static class RunCommand
    {
        public static int Execute(string[] args)
        {
            string? input = null;
            string? stdin = null;
            string? trace = null;
            uint? origin = null;
            uint? reset = null;
            int? ramSize = null;
            int? baud = null;
            int? cpuHz = null;
            int? patience = null;
            ulong limit = Cli.DefaultLimit;
            bool permissive = false;
            bool verbose = false;

            uint number;
            string text;
            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--origin":
                        if (!Cli.NextNumber(args, ref i, out number))
                            return Cli.Exit.Usage;
                        origin = number;
                        break;
                    case "--reset":
                        if (!Cli.NextNumber(args, ref i, out number))
                            return Cli.Exit.Usage;
                        reset = number;
                        break;
                    case "--ram-size":
                        if (!Cli.NextNumber(args, ref i, out number))
                            return Cli.Exit.Usage;
                        ramSize = (int)number;
                        break;
                    case "--baud":
                        if (!Cli.NextNumber(args, ref i, out number))
                            return Cli.Exit.Usage;
                        baud = (int)number;
                        break;
                    case "--cpu-hz":
                        if (!Cli.NextNumber(args, ref i, out number))
                            return Cli.Exit.Usage;
                        cpuHz = (int)number;
                        break;
                    case "--patience":
                        if (!Cli.NextNumber(args, ref i, out number))
                            return Cli.Exit.Usage;
                        patience = (int)number;
                        break;
                    case "--limit":
                        if (!Cli.NextNumber(args, ref i, out number))
                            return Cli.Exit.Usage;
                        limit = number;
                        break;
                    case "--stdin":
                        if (!Cli.NextText(args, ref i, out text))
                            return Cli.Exit.Usage;
                        stdin = text;
                        break;
                    case "--trace":
                        if (!Cli.NextText(args, ref i, out text))
                            return Cli.Exit.Usage;
                        trace = text;
                        break;
                    case "--permissive":
                        permissive = true;
                        break;
                    case "-v":
                    case "--verbose":
                        verbose = true;
                        break;
                    default:
                        if (args[i].StartsWith('-'))
                        {
                            Cli.Error($"unknown option '{args[i]}'");
                            return Cli.Exit.Usage;
                        }
                        if (input is not null)
                        {
                            Cli.Error("two input files given");
                            return Cli.Exit.Usage;
                        }
                        input = args[i];
                        break;
                }
            }

            if (input is null)
            {
                Cli.Error("no input file");
                Console.Error.WriteLine(Cli.Usage);
                return Cli.Exit.Usage;
            }

            byte[] image;
            try
            {
                image = File.ReadAllBytes(input);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Cli.Error($"cannot read '{input}': {ex.Message}");
                return Cli.Exit.Usage;
            }
            if (image.Length == 0)
            {
                Cli.Error($"'{input}' is empty");
                return Cli.Exit.Usage;
            }
            if (image.Length % 2 != 0)
                Cli.Warn("the image has an odd number of bytes, its last halfword is incomplete");
            if (ramSize is int size && (size < 2 || size > 1_000_000_000))
            {
                Cli.Error("--ram-size has to be a number of bytes between 2 and 1000000000");
                return Cli.Exit.Usage;
            }

            var defaults = new BusOptions();
            uint where = origin ?? reset ?? defaults.RamBase;
            if (where < BusOptions.DeviceWindowEnd)
            {
                //the low window belongs to the devices, an image there would be eaten by the uart
                Cli.Error($"0x{where:X8} is inside the device window, load the image into the ram (--origin 0x{defaults.RamBase:X8} is the default)");
                return Cli.Exit.Usage;
            }

            var options = new BusOptions
            {
                RamSize = ramSize ?? defaults.RamSize,
                //the machine starts where the image starts, unless --reset asks for another vector
                ResetVector = reset ?? where,
                Permissive = permissive,
            };
            var bus = new Bus(options);

            int loaded;
            try
            {
                loaded = ProgramImage.Load(bus, where, image);
            }
            catch (BusFault fault)
            {
                Cli.Error(fault.Message);
                return Cli.Exit.BusFault;
            }

            HostConsole console;
            try
            {
                console = stdin is string path ? HostConsole.FromFile(path) : HostConsole.Terminal();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Cli.Error($"cannot read '{stdin}': {ex.Message}");
                return Cli.Exit.Usage;
            }

            using (console)
            {
                var uart = new UartDevice(console, TxCyclesPerByte(baud ?? 0, cpuHz ?? Cli.DefaultCpuHz));
                bus.AddDevice(UartDevice.DeviceName, UartDevice.Block, uart);

                var cpu = new CPU(bus);

                StreamWriter? traceWriter = null;
                try
                {
                    if (trace is string tracePath)
                        traceWriter = new StreamWriter(tracePath);
                    if (verbose || traceWriter is not null)
                        cpu.OnRetire = step =>
                        {
                            traceWriter?.WriteLine(step);
                            if (verbose)
                                Console.Error.WriteLine(step);
                        };

                    var detector = new InputWaitDetector(uart, console, patience ?? Cli.DefaultPatience);
                    StepResult last = cpu.Last;
                    for (ulong done = 0; !cpu.Halted && done < limit; done++)
                    {
                        last = cpu.Step();
                        //the one hang worth reporting instead of waiting for Ctrl+C
                        if (detector.Stuck)
                            break;
                    }

                    console.Flush();
                    traceWriter?.Flush();

                    return Report(input, loaded, where, cpu, last, detector, stdin is not null, limit);
                }
                finally
                {
                    traceWriter?.Dispose();
                }
            }
        }

        //what the run produced and what the process exit code means
        private static int Report(string input, int loaded, uint origin, CPU cpu,
            StepResult last, InputWaitDetector detector, bool inputWasFile, ulong limit)
        {
            Cli.Error($"{input}: {loaded} bytes at 0x{origin:X8}, " +
                $"{cpu.InstructionsRetired} instructions retired, pc 0x{cpu.PC:X8} ({cpu.Bus.RegionName(cpu.PC)})");

            if (detector.Stuck)
            {
                Cli.Error(InputWaitDetector.Hint(inputWasFile));
                return Cli.Exit.InputWait;
            }
            if (!cpu.Halted)
            {
                Cli.Error($"stopped after {limit} instructions, the program is probably stuck in a loop");
                return Cli.Exit.Limit;
            }

            Cli.Error($"stopped at 0x{last.Address:X8}: {last.Message ?? last.Reason.ToString()}");
            return last.Reason switch
            {
                StopReason.Reti => Cli.Exit.Ok,
                StopReason.BusFault => Cli.Exit.BusFault,
                StopReason.IllegalInstruction => Cli.Exit.Illegal,
                StopReason.IntNotImplemented => Cli.Exit.Interrupt,
                _ => Cli.Exit.Usage,
            };
        }

        //one byte is ten bit times; the model has no clock, so it counts retired instructions
        private static int TxCyclesPerByte(int baud, int cpuHz)
        {
            if (baud <= 0 || cpuHz <= 0)
                return 0;
            long cycles = ((long)cpuHz * 10 + baud - 1) / baud;
            return cycles > int.MaxValue ? int.MaxValue : (int)cycles;
        }
    }
}
