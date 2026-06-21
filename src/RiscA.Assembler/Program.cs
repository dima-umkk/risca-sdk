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
        Console.WriteLine(srcline.Line);
        if(srcline.ParsedInstruction is ParsedInstruction pi)
        {
            foreach (var (index, instr) in pi.Instructions.Index())
            {
                Console.WriteLine($"> 0x{(srcline.Address+index*2):X4} {instr}");
            }
        }
    }
}


return 0;