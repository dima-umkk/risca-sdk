using RiscA.Core.HW;
using System;
using System.Globalization;
using System.Reflection;

namespace RiscA.Rasim
{
    //Shared parts of the rasim command line: usage text, number syntax, exit codes.
    internal static class Cli
    {
        //the default clock of the emulated machine, only used to turn --baud into cycles
        public const int DefaultCpuHz = 25_000_000;

        //the default of --limit: the run stops instead of spinning forever
        public const ulong DefaultLimit = 10_000_000;

        //how many reads of an empty rx_ready mean "this program is not going to get a byte"
        public const int DefaultPatience = 4096;

        //images live in the ram unless --origin says otherwise
        public const uint DefaultOrigin = BusOptions.DeviceWindowEnd;

        public static string Version
        {
            get
            {
                Version? v = Assembly.GetEntryAssembly()?.GetName().Version;
                return v is null ? "dev" : v.ToString(3);
            }
        }

        public const string Usage = """
            rasim <command> [options] [prog.bin]

            commands:
              run     load an image and run it, with the console uart in device block 0
              disasm  print an image as an instruction listing
              debug   the interactive debugger (milestone M2, not implemented yet)

            run options:
              prog.bin          the image, the raw little-endian halfwords as `rasm -o` writes them
              --origin <addr>   where the image goes, default 0x00010000 (the start of the ram)
              --reset <addr>    where the cpu starts, default: the origin
              --ram-size <n>    size of the ram in bytes, default 65536
              --stdin <file>    read the console input from a file instead of the terminal
              --baud <bits>     make the transmitter slow: one byte occupies 10 bit times
              --cpu-hz <hertz>  the emulated clock used by --baud, default 25000000
              --limit <n>       stop after this many retired instructions, default 10000000
              --patience <n>    reads of an empty rx_ready before the wait is called hopeless
              --trace <file>    write every retired instruction to a file
              -v                print every retired instruction while running
              --permissive      an unmapped access reads as 0 instead of stopping the machine

            disasm options:
              --origin <addr>   the address of the first halfword, default 0x00010000

            numbers are decimal, 0x... hexadecimal or 0b... binary.

            exit codes of run: 0 RETI, 2 instruction limit, 3 bus fault, 4 illegal instruction,
            5 INT (interrupts are not implemented), 6 the program waited for console input that
            cannot arrive. 1 means a usage or a file error.
            """;

        public static void Error(string message)
        {
            //the program writes its own output to stdout, everything rasim has to say goes to stderr,
            //so `rasim run prog.bin > out.txt` stays clean
            Console.Error.WriteLine($"rasim: {message}");
        }

        public static void Warn(string message) => Console.Error.WriteLine($"rasim: warning: {message}");

        //run reports why the program stopped; 1 is a usage or a file error
        public static class Exit
        {
            public const int Ok = 0;
            public const int Usage = 1;
            public const int Limit = 2;
            public const int BusFault = 3;
            public const int Illegal = 4;
            public const int Interrupt = 5;
            public const int InputWait = 6;
        }

        public static bool NextNumber(string[] args, ref int i, out uint value)
        {
            string option = args[i];
            value = 0;
            if (i + 1 >= args.Length || !TryNumber(args[i + 1], out value))
            {
                Error($"{option} needs a number");
                return false;
            }
            i++;
            return true;
        }

        public static bool NextText(string[] args, ref int i, out string value)
        {
            string option = args[i];
            value = "";
            if (i + 1 >= args.Length)
            {
                Error($"{option} needs a value");
                return false;
            }
            value = args[++i];
            return true;
        }

        //the assembler number syntax, so the same value reads the same in both tools
        public static bool TryNumber(string text, out uint value)
        {
            value = 0;
            string s = text.Replace("_", "");
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(s[2..], NumberStyles.HexNumber, null, out value);
            if (s.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                return ConvertBinary(s[2..], out value);
            return uint.TryParse(s, NumberStyles.None, null, out value);
        }

        private static bool ConvertBinary(string digits, out uint value)
        {
            value = 0;
            if (digits.Length == 0 || digits.Length > 32)
                return false;
            foreach (char c in digits)
            {
                if (c != '0' && c != '1')
                    return false;
                value = (value << 1) | (uint)(c - '0');
            }
            return true;
        }
    }
}
