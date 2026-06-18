using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace RiscA.Core.Asm
{

    public class AsmInstruction(int address, Instruction instr, string? labelRef = null)
    {
        public int Address { get; set; } = address;
        public Instruction Instruction { get; set; } = instr;
        public string? LabelRef { get; set; } = labelRef;
    }

    public class Parser
    {
        public Dictionary<int, AsmInstruction> AsmInstructions { get;  } = new Dictionary<int, AsmInstruction>();
        Dictionary<string, AsmInstruction> labelToInstr = new Dictionary<string, AsmInstruction>();
        int curAddress = 0;

        static List<TK> aluFunc = [];
        static List<(List<TK[]>, Func<Parser, List<TK[]>, List<Token>, int, List<Token>>)> rules =
            [
                ([[TK.MOV,TK.ADD,TK.SUB,TK.AND,TK.OR,TK.XOR,TK.NOT,TK.MUL], [TK.REG], [TK.COMMA], [TK.REG]], parseAlu)
            ];

        public void ParseLine(string filename, string line, int linePos)
        {
            List<Token> tokens = Tokenizer.tokenizeLine(filename, line, linePos);
            int pos = 0;
            bool matched = false;
            while (pos < tokens.Count)
            {
                foreach (var rule in rules)
                {
                    var (pattern, handler) = rule;
                    if (tokens.Count - pos < pattern.Count) continue;

                    bool ruleMatch = true;
                    for (int i = 0; i < pattern.Count; i++)
                    {
                        if (Array.IndexOf(pattern[i], tokens[pos + i].TokenType) < 0)
                        {
                            ruleMatch = false;
                            break;
                        }
                    }

                    if (ruleMatch)
                    {
                        tokens = handler(this, pattern, tokens, pos);
                        matched = true;
                        pos = 0;
                        break;
                    }
                }

                if (!matched) pos++;
            }

            if (matched)
                curAddress += 2; 
            else
                throw new Exception($"Parse error: {filename}: line: {linePos}");
        }

        public void AddInstruction(Instruction i)
        {
            AsmInstruction asmInstr = AsmInstructions.GetValueOrDefault(curAddress, new AsmInstruction(curAddress, i));
            asmInstr.Instruction = i;
            AsmInstructions[curAddress] = asmInstr;
        }

        static List<Token> parseAlu(Parser parser, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(OpCode.ALU_REG_REG)
                .withFunc3(rule[0].IndexOf(tokens[pos].TokenType))
                .withRd(tokens[pos + 1].intValue)
                .withRs(tokens[pos + 3].intValue);
            parser.AddInstruction(i);
            tokens.RemoveRange(pos, 4);
            return tokens;
        }
    }
}
