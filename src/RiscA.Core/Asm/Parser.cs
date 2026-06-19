using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.InteropServices;
using System.Text;

namespace RiscA.Core.Asm
{

    public class ParsedInstruction()
    {
        public List<Instruction> Instructions { get; init; } = [];
        public string? Label { get; set; }
        public string? RefLabel { get; set; }
    }

    public class Parser
    {
        static List<(List<TK[]>, Func<ParsedInstruction, List<TK[]>, List<Token>, int, List<Token>>)> rules =
            [
                ([ [TK.MOV,TK.ADD,TK.SUB,TK.AND,TK.OR,TK.XOR,TK.NOT,TK.MUL], [TK.REG], [TK.COMMA], [TK.REG] ], ParseAlu),
                ([ [TK.SHL, TK.SHR, TK.ADD, TK.SUB], [TK.REG], [TK.COMMA], [TK.NUMBER] ], ParseAluImm),
                ([ [TK.MOVI, TK.MOVL], [TK.REG], [TK.COMMA], [TK.NUMBER] ], ParseRegImm),
                ([ [TK.LDB, TK.LDW], [TK.REG], [TK.COMMA], [TK.LSBRACKET], [TK.REG], [TK.PLUS], [TK.NUMBER], [TK.RSBRACKET] ], ParseLD),
                ([ [TK.STB, TK.STW], [TK.LSBRACKET], [TK.REG], [TK.PLUS], [TK.NUMBER], [TK.RSBRACKET], [TK.COMMA], [TK.REG] ], ParseST),
            ];

        public ParsedInstruction ParseLine(string filename, string line, int linePos)
        {
            List<Token> tokens = Tokenizer.tokenizeLine(filename, line, linePos);
            ParsedInstruction parsedInstruction = new ParsedInstruction();
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
                        tokens = handler(parsedInstruction, pattern, tokens, pos);
                        matched = true;
                        pos = 0;
                        break;
                    }
                }

                if (!matched) pos++;
            }

            if (!matched)
                throw new Exception($"Parse error: {filename}: line: {linePos}");

            return parsedInstruction;
        }

        static List<Token> ParseAlu(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(OpCode.ALU_REG_REG)
                .withFunc3(rule[0].IndexOf(tokens[pos].TokenType))
                .withRd(tokens[pos + 1].intValue)
                .withRs(tokens[pos + 3].intValue);
            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 4);
            return tokens;
        }

        static List<Token> ParseAluImm(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(OpCode.ALU_REG_IMM)
                .withFunc2(rule[0].IndexOf(tokens[pos].TokenType))
                .withRd(tokens[pos + 1].intValue)
                .withImm7(tokens[pos + 3].intValue);
            if ((i.Func2 == 0 || i.Func2 == 1) && (tokens[pos + 3].intValue < 0 || tokens[pos + 3].intValue > 32))
                throw new Exception($"Number should be 0 .. 32 for {tokens[pos].TokenString} {tokens[pos + 1].TokenString}, {tokens[pos + 3].TokenString}");
            if ((i.Func2 == 2 || i.Func2 == 3) && (tokens[pos + 3].intValue < 0 || tokens[pos + 3].intValue > 127))
                throw new Exception($"Number should be 0 .. 127 for {tokens[pos].TokenString} {tokens[pos + 1].TokenString}, {tokens[pos + 3].TokenString}");
            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 4);
            return tokens;
        }

        static List<Token> ParseRegImm(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(OpCode.REG_IMM)
                .withFunc1(rule[0].IndexOf(tokens[pos].TokenType))
                .withRd(tokens[pos + 1].intValue)
                .withImm8(tokens[pos + 3].intValue);
            if (tokens[pos + 3].intValue > 255)
                throw new Exception($"Number should be <= 255 for {tokens[pos].TokenString} {tokens[pos + 1].TokenString}, {tokens[pos + 3].TokenString}");
            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 4);
            return tokens;
        }
        static List<Token> ParseLD(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(OpCode.ST_LD)
                .withFunc21(0)
                .withFunc22(rule[0].IndexOf(tokens[pos].TokenType))
                .withRd(tokens[pos + 1].intValue)
                .withRs(tokens[pos + 4].intValue)
                .withImm3(tokens[pos + 6].intValue);
            if (tokens[pos + 6].intValue < 0 || tokens[pos + 6].intValue > 7)
                throw new Exception($"Number should be 0 .. 7 for '{tokens[pos].TokenString} {tokens[pos + 1].TokenString}, [{tokens[pos + 4].TokenString} + {tokens[pos + 6].intValue}]'");
            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 8);
            return tokens;
        }

        static List<Token> ParseST(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(OpCode.ST_LD)
                .withFunc21(1)
                .withFunc22(rule[0].IndexOf(tokens[pos].TokenType))
                .withRd(tokens[pos + 7].intValue)
                .withRs(tokens[pos + 2].intValue)
                .withImm3(tokens[pos + 4].intValue);
            if (tokens[pos + 4].intValue < 0 || tokens[pos + 4].intValue > 7)
                throw new Exception($"Number should be 0 .. 7 for '{tokens[pos].TokenString} {tokens[pos + 1].TokenString}, [{tokens[pos + 4].TokenString} + {tokens[pos + 6].intValue}]'");
            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 8);
            return tokens;
        }

    }
}
