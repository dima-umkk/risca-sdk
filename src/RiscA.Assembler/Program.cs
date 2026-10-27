using RiscA.Core;
using RiscA.Core.Asm;
using RiscA.Core.ISA;

string? filename = null;
string? binfile = null;
string? lstfile = null;
int origin = 0;

for (int i=0; i<args.Length; i++)
{
    if (args[i].Equals("--version") || args[i].Equals("-V"))
    {
        string? version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3);
        Console.WriteLine($"rasm {version ?? "dev"}");
        return 0;
    }
    if (args[i].Equals("-i") && args.Length > i + 1)
    {
        filename = args[i+1];
    }
    if (args[i].Equals("-o") && args.Length > i + 1)
    {
        binfile = args[i + 1];
    }
    if (args[i].Equals("-l") && args.Length > i + 1)
    {
        lstfile = args[i + 1];
    }
    if (args[i].Equals("-a") && args.Length > i + 1)
    {
        //pool dw entries hold absolute addresses, so assemble for the address the image is loaded at
        origin = args[i + 1].StartsWith("0x") ? (int)Convert.ToInt64(args[i + 1][2..], 16) : int.Parse(args[i + 1]);
    }
    if (args[i].StartsWith("-v"))
    {
        Verbose.ParserMatches = args[i].Contains('p');
        Verbose.AssemblerInstructions = args[i].Contains('i');
    }
}

if(filename == null)
{
    Console.WriteLine("Usage: rasm.exe -i filename.rasm [-o filename.bin] [-l filename.lst] [-a origin] [-vi]");
    Console.WriteLine("       rasm.exe --version");
    return -1;
}
using BinaryWriter? binwriter = binfile is not null ? new BinaryWriter(File.OpenWrite(binfile)) : null;
using StreamWriter? lstwriter = lstfile is not null ? new StreamWriter(lstfile) : null;

Assembler asm = new(origin);
try
{
    asm.Compile(filename);
}
catch(Exception ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    return -1;
}

int binsize = 0;

foreach(SrcLine srcline in asm.Src)
{
    if(srcline.ParsedInstruction is ParsedInstruction pi)
    {
        if (pi.Instructions.Count == 0)
        {
            Console.WriteLine($"                                     {srcline.Line}");
            lstwriter?.WriteLine($"                                     {srcline.Line}");
        }
        else
        {    
            foreach (var (index, instr) in pi.Instructions.Index())
            {
                int instraddr = srcline.Address + index * 2;
                string refaddr = instr.OpCode switch
                {
                    OpCode.BRANCH => $" ({instr.CalcRef(instraddr):X4})",
                    OpCode.LDI => $" ({instr.CalcRef(instraddr):X4})",
                    OpCode.CALL_JMP_RET => (CallJmpRetFunc)instr.Func2 switch
                    {
                        CallJmpRetFunc.CALL_IMM or CallJmpRetFunc.JR => $" ({instr.CalcRef(instraddr):X4})",
                        _ => ""
                    },
                    _ => ""
                };
                string sourcecode = index switch
                {
                    0 => srcline.Line,
                    _ => "",
                };
                string instrstr = instr.ToString();
                string instrtrim = new string(' ', Math.Max(1, 15 - refaddr.Length - instrstr.Length));
                if (Verbose.AssemblerInstructions)
                {
                    if (pi.IsDW || pi.IsDB)
                        Console.WriteLine($"0x{instraddr:X8} {instr.Raw:X4}                      {sourcecode}");
                    else
                        Console.WriteLine($"0x{instraddr:X8} {instr.Raw:X4}  {instrstr}{refaddr}{instrtrim}{sourcecode}");
                }
                if (pi.IsDW || pi.IsDB)
                    lstwriter?.WriteLine($"0x{instraddr:X8} {instr.Raw:X4}                       {sourcecode}");
                else
                    lstwriter?.WriteLine($"0x{instraddr:X8} {instr.Raw:X4}  {instrstr}{refaddr}{instrtrim}{sourcecode}");
                binwriter?.Write(instr.Raw);
                binsize += 2;
            }
        }
    }
}

Console.WriteLine($"\nBinary size: {binsize} bytes.");

return 0;