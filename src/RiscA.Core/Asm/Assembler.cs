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
        
        public List<SrcLine> Src { get {  return src; }  }
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
                    throw new Exception($"{filename}:{i}: {ex.Message}");
                }
            }

            //2nd pass: process labels
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i].ParsedInstruction is ParsedInstruction pi && pi.RefLabel is string reflabel)
                {
                    SrcLine? refline;
                    if (!labelMap.TryGetValue(reflabel, out refline))
                    {
                        throw new Exception($"{src[i].Filename}: {src[i].Pos}: Unknonw label {reflabel}");
                    }
                    int refbytes = refline.Address - src[i].Address;
                    int refinstr = refbytes >> 1;
                    int refwords = refbytes >> 2;
                    int imm = pi.Instructions[0].OpCode == ISA.OpCode.CALL_JMP_RET ? refwords : refinstr;
                    if(pi.Instructions[0].OpCode == ISA.OpCode.CALL_JMP_RET && (refbytes & 0b0000_0011) != 0)
                    {
                        throw new Exception($"{src[i].Filename}: {src[i].Pos}: Call {reflabel} is not word aligned!");
                    }
                    try
                    {
                        pi.Instructions[0].CheckImmLimits(imm);
                    }
                    catch(Exception ex)
                    {
                        throw new Exception($"{src[i].Filename}: {src[i].Pos}: Reference address to far ({imm})! {ex.Message}");
                    }
                    pi.Instructions[0] = pi.Instructions[0].SetImm(imm);
                }

            }
        }
    }
}
