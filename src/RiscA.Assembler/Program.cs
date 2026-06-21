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

Assembler asm = new Assembler(0);
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
    foreach(SrcLine srcline in asm.Src)
    {
        Console.WriteLine($"> {srcline.Line}");
        if(srcline.ParsedInstruction is ParsedInstruction pi)
        {
            foreach (var (index, instr) in pi.Instructions.Index())
            {
                int instraddr = srcline.Address + index * 2;
                string refaddr = instr.OpCode switch
                {
                    OpCode.BRANCH => $" -> 0x{(instraddr + instr.Imm7s*2):X4}",
                    OpCode.LDI => $" -> 0x{(instraddr + instr.Imm9*2):X4}",
                    OpCode.CALL_JMP_RET => (CallJmpRetFunc)instr.Func2 switch
                    {
                        CallJmpRetFunc.CALL_IMM or CallJmpRetFunc.JR => $" -> 0x{(instraddr + instr.ImmCallJr * 4):X4}",
                        _ => ""
                    },
                    _ => ""
                };
                Console.WriteLine($"0x{instraddr:X4} {instr} {refaddr}");
            }
        }
    }
}


return 0;