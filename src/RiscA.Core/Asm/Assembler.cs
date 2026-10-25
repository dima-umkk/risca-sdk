using RiscA.Core.ISA;
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
        List<SrcLine> src = [];

        Dictionary<string, SrcLine> labelMap = [];

        public void Compile(string filename)
        {
            var pre = new Preprocessor();
            pre.Process(filename);

            src.Clear();
            src.AddRange(pre.Lines);

            //1st pass: parse lines
            for (int i = 0; i < src.Count; i++)
            {
                try
                {
                    if (src[i].ParsedInstruction is not null)
                    {//.equ and .include lines produce no code, so they are not parsed
                        src[i].Address = curAddress;
                        continue;
                    }
                    if(src[i].Line.Contains(".func"))
                    {//function declaration
                        src[i].Line = src[i].Line.Replace(".func", "");
                        if((curAddress & 0b0000_0011) != 0)
                        {//make work aligned
                            var nops = new SrcLine(";word align", i, src[i].Filename);
                            nops.ParsedInstruction = new ParsedInstruction();
                            nops.ParsedInstruction.Instructions.Add(new Instruction(0));
                            nops.Address = curAddress;
                            curAddress += 2;
                            src.Insert(i, nops);
                            continue;
                        }
                    }
                    var pi = src[i].ParsedInstruction = Parser.ParseLine(src[i].Line, pre.Constants);
                    src[i].Address = curAddress;
                    curAddress += pi.Instructions.Count * 2;
                    if (pi.Label is not null)
                    {
                        labelMap[pi.Label] = src[i];
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"{src[i].Filename}:{src[i].Pos + 1}: {ex.Message}");
                }
            }

            //2nd pass: process labels
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i].ParsedInstruction is ParsedInstruction pi && pi.RefLabel is string reflabel)
                {
                    if (!labelMap.TryGetValue(reflabel, out SrcLine? refline))
                    {
                        throw new Exception($"{src[i].Filename}: {src[i].Pos + 1}: Unknown label '{reflabel}'");
                    }
                    if (pi.IsDW)
                    {
                        int addr = refline.Address;
                        pi.Instructions[0] = new Instruction(unchecked((ushort)(addr & 0xFFFF)));
                        pi.Instructions[1] = new Instruction(unchecked((ushort)((addr >> 16) & 0xFFFF)));
                    }
                    else
                    {
                        int refbytes = refline.Address - src[i].Address;
                        int refinstr = refbytes >> 1;
                        int refwords = (refline.Address >> 2) - (src[i].Address >> 2);
                        //CALL steps in 4 byte words and needs a word aligned target (spec line 124),
                        //JR steps in instructions (spec line 127), so only CALL uses the word offset
                        bool jumpByWord = pi.Instructions[0].OpCode == ISA.OpCode.CALL_JMP_RET
                            && (CallJmpRetFunc)pi.Instructions[0].Func2 == CallJmpRetFunc.CALL_IMM;
                        int imm = jumpByWord ? refwords : refinstr;
                        if (jumpByWord && (refline.Address & 0b0000_0011) != 0)
                        {
                            throw new Exception($"{src[i].Filename}: {refline.Pos + 1}: '{reflabel}' is not word aligned!");
                        }
                        try
                        {
                            pi.Instructions[0].CheckImmLimits(imm);
                        }
                        catch (Exception ex)
                        {
                            throw new Exception($"{src[i].Filename}: {src[i].Pos + 1}: Reference address to far ({imm})! {ex.Message}");
                        }
                        pi.Instructions[0] = pi.Instructions[0].SetImm(imm);
                    }
                }
            }
        }
    }
}
