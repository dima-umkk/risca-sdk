using RiscA.Core.HW;
using System;
using System.IO;

namespace RiscA.Rasim
{

    //rasim run: load an image, attach the default devices and run until stop, fault or limit.
    internal static class RunCommand
    {
        public static int Execute(string[] args)
        {
            string? input = null;
            string? stdin = null;
            string? trace = null;
            uint? origin = null;
            uint? reset = null;
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

            uint where = origin ?? reset ?? Ram.DefaultBase;
            if (where < Ram.DefaultBase)
            {
                //the low window belongs to the devices, an image there would be eaten by the uart
                Cli.Error($"0x{where:X8} is inside the device window, load the image into the ram (--origin 0x{Ram.DefaultBase:X8} is the default)");
                return Cli.Exit.Usage;
            }

            var options = new Bus.Options
            {
                //the machine starts where the image starts, unless --reset asks for another vector
                ResetVector = reset ?? where,
                Permissive = permissive,
            };

            Stream consoleInput;
            try
            {
                consoleInput = stdin is string path ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : Console.OpenStandardInput();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Cli.Error($"cannot read '{stdin}': {ex.Message}");
                return Cli.Exit.Usage;
            }

            var stdout = Console.OpenStandardOutput();
            var bus = new Bus(options)
                .AddDevice(new Ram())
                .AddDevice(new UartDevice(consoleInput, stdout));

            int loaded;
            try
            {
                loaded = ProgramImage.Load(bus, where, image);
            }
            catch (Bus.Fault fault)
            {
                Cli.Error(fault.Message);
                consoleInput.Dispose();
                return Cli.Exit.BusFault;
            }

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

                var last = cpu.Run(limit);

                stdout.Flush();
                traceWriter?.Flush();

                return Report(input, loaded, where, cpu, last, limit);
            }
            finally
            {
                traceWriter?.Dispose();
                consoleInput.Dispose();
            }
        }

        //what the run produced and what the process exit code means
        private static int Report(string input, int loaded, uint origin, CPU cpu, StepResult last, ulong limit)
        {
            Cli.Error($"{input}: {loaded} bytes at 0x{origin:X8}, " +
                $"{cpu.InstructionsRetired} instructions retired, pc 0x{cpu.PC:X8} ({cpu.Bus.RegionName(cpu.PC)})");

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
    }
}
