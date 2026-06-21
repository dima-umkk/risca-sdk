using RiscA.Core.Asm;

string[] lines;
string filename;
if (args[0].Length > 1 && args[0].Equals("-i"))
{
    filename = args[1];
    lines = File.ReadAllLines(args[1]);
}
else 
{
    Console.WriteLine("Usage: rasm.exe -i filename.rasm");
    return -1;
}

Parser parser = new Parser();
for(int i=0; i<lines.Length; i++)
{
    try
    {
        Console.WriteLine($">{lines[i]}");
        ParsedInstruction pi = parser.ParseLine(filename, lines[i], i);
    }
    catch(Exception ex)
    {
        Console.WriteLine($"ERROR: {filename}:{i}: {ex.Message}");
        return -1;
    }
        
}    

return 0;