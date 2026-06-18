using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace RiscA.Core.Asm
{

    public class AsmInstruction(int address, Instruction instr, string labelRef);

    public class Parser
    {
        List<AsmInstruction> asmInstructions = new List<AsmInstruction>();
        Dictionary<string, AsmInstruction> labelToInstr = new Dictionary<string, AsmInstruction>();

        List<(List<TK>, Func<List<TK>, int, List<TK>>)> rules =
            [
                ([TK.MOV, TK.REG], new Func<List<TK>, int, List<TK>>(parseAlu))
            ];

        public void ParseLine(string filename, string line, int linePos)
        {
            List<Token> tokens = Tokenizer.tokenizeLine(filename, line, linePos);

        }

        List<TK> parseAlu(List<TK> tokens, int pos)
        {
            return tokens;
        }
    }
}
