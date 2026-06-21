using RiscA.Core;
using RiscA.Core.Asm;
using RiscA.Core.ISA;

string[]? lines = null;
string? filename = null;

for(int i=0; i<args.Length; i++)
{
    if (args[i].Equals("-i") && args.Length > i + 1)
    {
        filename = args[i+1];
        lines = File.ReadAllLines(filename);
    }
    if (args[i].StartsWith("-v"))
    {
        Verbose.ParserMatches = args[i].Contains("p");
        Verbose.AssemblerInstructions = args[i].Contains("i");
    }
}

if(filename == null || lines == null)
{
    Console.WriteLine("Usage: rasm.exe -i filename.rasm");
    return -1;
}

for(int i=0; i<lines.Length; i++)
{
    try
    {
        ParsedInstruction pi = Parser.ParseLine(lines[i]);
        if (Verbose.AssemblerInstructions)
        {
            Console.WriteLine($">{lines[i]}");
            foreach (Instruction instr in pi.Instructions)
            {
                Console.WriteLine($">>{instr}");
            }
        }
    }
    catch(Exception ex)
    {
        Console.WriteLine($"ERROR: {filename}:{i}: {ex.Message}");
        return -1;
    }
        
}    

return 0;