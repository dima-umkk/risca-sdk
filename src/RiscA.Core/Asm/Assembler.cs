using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Core.Asm
{
    public class SrcLine(string line, int pos, string filename, int address = 0, ParsedInstruction? parsedInstruction = null)
    {
        public int Address { get; set; } = address;
        public string Line { get; set; } = line;
        public int Pos { get; set; } = pos;

        public string Filename { get; set; } = filename;
        public ParsedInstruction? ParsedInstruction { get; set; } = parsedInstruction;
    }

    public class Assembler(int address)
    {
        int curAddress = address;
        List<SrcLine> src = new List<SrcLine>();
        Dictionary<string, SrcLine> labelMap = new Dictionary<string, SrcLine>();

        public void Compile(string filename)
        {
            string[] lines = File.ReadAllLines(filename);
            src.AddRange(lines.Select((l, i) => new SrcLine(l, i, filename)).ToList());

            //TODO: make preprocessor pass for constans and includes

            //1st pass: parse lines
            for (int i = 0; i < src.Count; i++)
            {
                try
                {
                    var pi = src[i].ParsedInstruction = Parser.ParseLine(src[i].Line);
                    src[i].Address = curAddress;
                    curAddress += pi.Instructions.Count * 2;
                    if (pi.Label is not null)
                    {
                        labelMap[pi.Label] = src[i];
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"ERROR: {filename}:{i}: {ex.Message}");
                }
            }

            //2nd pass: process labels
            for (int i = 0; i < src.Count; i++)
            {


            }
        }
    }
}
