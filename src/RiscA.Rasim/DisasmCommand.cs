using RiscA.Core.ISA;
using RiscA.Core.HW;
using System;
using System.IO;

namespace RiscA.Rasim
{
    //rasim disasm: decode the little-endian halfwords into a listing like rasm -vi prints, run nothing.
    internal static class DisasmCommand
    {
        public static int Execute(string[] args)
        {
            string? input = null;
            uint? origin = null;
            uint number;

            for (int i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--origin":
                        if (!Cli.NextNumber(args, ref i, out number))
                            return Cli.Exit.Usage;
                        origin = number;
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

            uint at = origin ?? Cli.DefaultOrigin;
            for (int i = 0; i + 1 < image.Length; i += 2)
            {
                ushort raw = (ushort)(image[i] | (image[i + 1] << 8));
                uint address = at + (uint)i;
                var inst = new Instruction(raw);
                Console.WriteLine($"0x{address:X8} {raw:X4}  {inst}{Reference(inst, (int)address)}");
            }
            return Cli.Exit.Ok;
        }

        //the jump target in parentheses, like the assembler listing
        private static string Reference(Instruction inst, int address)
        {
            bool referenced = inst.OpCode switch
            {
                OpCode.BRANCH or OpCode.LDI => true,
                OpCode.CALL_JMP_RET => (CallJmpRetFunc)inst.Func2 is CallJmpRetFunc.CALL_IMM or CallJmpRetFunc.JR,
                _ => false,
            };
            return referenced ? $" ({inst.CalcRef(address):X4})" : "";
        }
    }
}
