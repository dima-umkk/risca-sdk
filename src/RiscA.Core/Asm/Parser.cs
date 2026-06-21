using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.Data;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

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
                //Expressions
                ([ [TK.NUMBER], [TK.SLASH,TK.ASTER,TK.MINUS,TK.PLUS], [TK.NUMBER] ], ExpMath),
                ([ [TK.MINUS], [TK.NUMBER] ], ExpNegative),
                ([ [TK.LPAREN], [TK.NUMBER], [TK.RPAREN] ], ExpParen),

                //Instructions
                ([ [TK.MOV,TK.ADD,TK.SUB,TK.AND,TK.OR,TK.XOR,TK.NOT,TK.MUL], [TK.REG], [TK.COMMA], [TK.REG], [TK.EOL] ], ParseAlu),
                ([ [TK.SHL, TK.SHR, TK.ADD, TK.SUB], [TK.REG], [TK.COMMA], [TK.NUMBER], [TK.EOL] ], ParseAluImm),
                ([ [TK.MOVI, TK.MOVL], [TK.REG], [TK.COMMA], [TK.NUMBER], [TK.EOL] ], ParseRegImm),
                ([ [TK.LDB, TK.LDW], [TK.REG], [TK.COMMA], [TK.LSBRACKET], [TK.REG], [TK.PLUS], [TK.NUMBER], [TK.RSBRACKET], [TK.EOL] ], ParseLD),
                ([ [TK.STB, TK.STW], [TK.LSBRACKET], [TK.REG], [TK.PLUS], [TK.NUMBER], [TK.RSBRACKET], [TK.COMMA], [TK.REG], [TK.EOL] ], ParseST),
                ([ [TK.BEQZ, TK.BNEZ, TK.BGTZ, TK.BLTZ,], [TK.REG], [TK.COMMA], [TK.NUMBER, TK.LITERAL], [TK.EOL] ], ParseBranch),
                ([ [TK.LDI], [TK.REG], [TK.COMMA], [TK.NUMBER,TK.LITERAL], [TK.EOL] ], ParseLDI),
            ];

        public ParsedInstruction ParseLine(string filename, string line, int linePos)
        {
            List<Token> tokens = Tokenizer.tokenizeLine(filename, line, linePos);
            ParsedInstruction parsedInstruction = new ParsedInstruction();
            int pos = 0;
            List<Token> stack = new List<Token>();
            while (pos < tokens.Count)
            {
                stack.Add(tokens[pos++]);
                while (ProcessToken(stack, parsedInstruction))
                    ;
            }

            if (stack.Count > 0) //TODO: find most matched rule to clarify error
                throw new Exception($"Parse error: {filename}: line: {linePos}");

            return parsedInstruction;
        }

        private bool ProcessToken(List<Token> stack, ParsedInstruction pi)
        {
            foreach(var rule in rules)
            {
                var (pattern, handler) = rule;
                if (pattern.Count > stack.Count)
                    continue;
                bool ruleMatch = true;
                for (int i = 0; i < pattern.Count; i++)
                {
                    if (Array.IndexOf(pattern[i], stack[stack.Count - pattern.Count + i].TokenType) < 0)
                    {
                        ruleMatch = false;
                        break;
                    }
                }
                if (ruleMatch)
                {
                    stack = handler(pi, pattern, stack, stack.Count - pattern.Count);
                    return true;
                }
            }
            return false;
        }

        static List<Token> ParseAlu(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(OpCode.ALU_REG_REG)
                .withFunc3(rule[0].IndexOf(tokens[pos].TokenType))
                .withRd(tokens[pos + 1].intValue)
                .withRs(tokens[pos + 3].intValue);
            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 5);
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
            tokens.RemoveRange(pos, 5);
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
            tokens.RemoveRange(pos, 5);
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
            tokens.RemoveRange(pos, 9);
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
            tokens.RemoveRange(pos, 9);
            return tokens;
        }

        static List<Token> ParseBranch(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var imm7 = tokens[pos + 3].TokenType == TK.NUMBER ? tokens[pos + 3].intValue : 0;
            if (tokens[pos + 3].TokenType == TK.LITERAL)
                parsedInstruction.RefLabel = tokens[pos + 3].TokenString;
            var i = new Instruction(0)
                .withOpCode(OpCode.BRANCH)
                .withFunc2(rule[0].IndexOf(tokens[pos].TokenType))
                .withRd(tokens[pos + 1].intValue)
                .withImm7(imm7);

            if (imm7 < -64 || imm7 > 63)
                throw new Exception($"Number should be -64 .. 63 for '{tokens[pos].TokenString} {tokens[pos + 1].TokenString}, {tokens[pos + 3].TokenString}'");

            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 5);
            return tokens;
        }

        static List<Token> ParseLDI(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            int imm9 = tokens[pos + 3].TokenType == TK.NUMBER ? tokens[pos + 3].intValue : 0;
            if (tokens[pos + 3].TokenType == TK.LITERAL)
                parsedInstruction.RefLabel = tokens[pos + 3].TokenString;
            var i = new Instruction(0)
                .withOpCode(OpCode.LDI)
                .withRd(tokens[pos + 1].intValue)
                .withImm9(tokens[pos + 3].intValue);

            if (imm9 < -512 || imm9 > 511)
                throw new Exception($"Number should be -512 .. 511 for '{tokens[pos].TokenString} {tokens[pos + 1].TokenString}, {tokens[pos + 3].TokenString}'");

            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 5);
            return tokens;
        }

        static List<Token> ExpMath(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            int a = tokens[pos + 0].intValue;
            int b = tokens[pos + 2].intValue;

            int c = tokens[pos + 1].TokenType switch
            {
                TK.PLUS => a + b,
                TK.MINUS => a - b,
                TK.ASTER => a * b,
                TK.SLASH => a / b,
                _ => throw new Exception($"Unexpected math operation '{tokens[pos + 1].TokenString}' in '{tokens[pos].TokenString} {tokens[pos + 1].TokenString} {tokens[pos + 2].TokenString}'")
            };
            tokens.RemoveRange(pos, 3);
            tokens.Insert(pos, new Token(TK.NUMBER, c.ToString(), pos, c));
            return tokens;
        }

        static List<Token> ExpParen(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            Token number = tokens[pos + 1];
            tokens.RemoveRange(pos, 3);
            tokens.Insert(pos, number);
            return tokens;
        }

        static List<Token> ExpNegative(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            Token number = tokens[pos + 1];
            number.intValue = 0 - number.intValue;
            number.TokenString = number.intValue.ToString();
            tokens.RemoveRange(pos, 2);
            tokens.Insert(pos, number);
            return tokens;
        }
    }
}
