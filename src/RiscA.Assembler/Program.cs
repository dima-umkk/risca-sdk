using RiscA.Core;
using RiscA.Core.Asm;
using RiscA.Core.ISA;

string? filename = null;

for(int i=0; i<args.Length; i++)
{
    if (args[i].Equals("-i") && args.Length > i + 1)
    {
        filename = args[i+1];
    }
    if (args[i].StartsWith("-v"))
    {
        Verbose.ParserMatches = args[i].Contains("p");
        Verbose.AssemblerInstructions = args[i].Contains("i");
    }
}

if(filename == null)
{
    Console.WriteLine("Usage: rasm.exe -i filename.rasm");
    return -1;
}

Assembler asm = new(0);
try
{
    asm.Compile(filename);
}
catch(Exception ex)
{
    Console.WriteLine($"ERROR: {ex.Message}");
    return -1;
}

if (Verbose.AssemblerInstructions)
{
    int binsize = 0;
    foreach(SrcLine srcline in asm.Src)
    {
        if(srcline.ParsedInstruction is ParsedInstruction pi)
        {
            if (pi.Instructions.Count == 0)
            {
                Console.WriteLine($"                                     {srcline.Line}");
            }
            else
            {    
                foreach (var (index, instr) in pi.Instructions.Index())
                {
                    int instraddr = srcline.Address + index * 2;
                    string refaddr = instr.OpCode switch
                    {
                        OpCode.BRANCH => $" ({(instraddr + instr.Imm7s * 2):X4})",
                        OpCode.LDI => $" ({(instraddr + instr.Imm9 * 2):X4})",
                        OpCode.CALL_JMP_RET => (CallJmpRetFunc)instr.Func2 switch
                        {
                            CallJmpRetFunc.CALL_IMM or CallJmpRetFunc.JR => $" ({(instraddr + instr.ImmCallJr * 4):X4})",
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
                    Console.WriteLine($"0x{instraddr:X8} {instr.Raw:X4}  {instrstr}{refaddr}{instrtrim}{sourcecode}");
                    binsize += 2;
                }
            }
        }
    }
    Console.WriteLine($"\nBinary size: {binsize} bytes.");
}


return 0;