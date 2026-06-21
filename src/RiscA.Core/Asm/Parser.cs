using RiscA.Core.ISA;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using static System.Runtime.InteropServices.JavaScript.JSType;

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
        static List<(List<TK[]>, Func<ParsedInstruction, List<TK[]>, List<Token>, int, List<Token>?>)> rules =
            [
                //Expressions
                ([ [TK.MINUS], [TK.NUMBER] ], ExpNegative),
                ([ [TK.LPAREN], [TK.NUMBER], [TK.RPAREN] ], ExpParen),
                ([ [TK.NUMBER], [TK.ASTER, TK.SLASH], [TK.NUMBER] ], ExpMath),
                ([ [TK.NUMBER], [TK.MINUS], [TK.NUMBER] ], ExpMath),
                ([ [TK.NUMBER], [TK.PLUS], [TK.NUMBER] ], ExpMath),
                ([ [TK.LITERAL], [TK.COLON] ], ExpLabel),

                //Instructions
                ([ [TK.MOV,TK.ADD,TK.SUB,TK.AND,TK.OR,TK.XOR,TK.NOT,TK.MUL], [TK.REG], [TK.COMMA], [TK.REG], [TK.EOL] ], ParseAlu),
                ([ [TK.SHL, TK.SHR, TK.ADD, TK.SUB], [TK.REG], [TK.COMMA], [TK.NUMBER], [TK.EOL] ], ParseAluImm),
                ([ [TK.MOVI, TK.MOVL], [TK.REG], [TK.COMMA], [TK.NUMBER], [TK.EOL] ], ParseRegImm),
                ([ [TK.LDB, TK.LDW], [TK.REG], [TK.COMMA], [TK.LSBRACKET], [TK.REG], [TK.PLUS], [TK.NUMBER], [TK.RSBRACKET], [TK.EOL] ], ParseLD),
                ([ [TK.STB, TK.STW], [TK.LSBRACKET], [TK.REG], [TK.PLUS], [TK.NUMBER], [TK.RSBRACKET], [TK.COMMA], [TK.REG], [TK.EOL] ], ParseST),
                ([ [TK.BEQZ, TK.BNEZ, TK.BGTZ, TK.BLTZ,], [TK.REG], [TK.COMMA], [TK.NUMBER, TK.LITERAL], [TK.EOL] ], ParseBranch),
                ([ [TK.LDI], [TK.REG], [TK.COMMA], [TK.NUMBER,TK.LITERAL], [TK.EOL] ], ParseLDI),
                ([ [TK.CALL, TK.JR], [TK.NUMBER, TK.LITERAL], [TK.EOL] ], ParseCallJr),
                ([ [TK.CALL, TK.JMP, TK.INT], [TK.REG], [TK.EOL] ], ParseCallJmpInt),
                ([ [TK.RET, TK.RETI], [TK.EOL] ], ParseRet),
                ([ [TK.MOV], [TK.REG], [TK.COMMA], [TK.EPC], [TK.EOL] ], ParseEPC),
                ([ [TK.MOV], [TK.EPC], [TK.COMMA], [TK.REG], [TK.EOL] ], ParseEPC),
                ([ [TK.NOP], [TK.EOL] ], ParseNop),

                //Skip rules
                ([ [TK.EOL] ], Skip), //empty line
            ];

        public static ParsedInstruction ParseLine(string line)
        {
            List<Token> tokens = Tokenizer.tokenizeLine(line);
            ParsedInstruction parsedInstruction = new ParsedInstruction();
            while (ProcessToken(tokens, parsedInstruction))
                ;

            if (tokens.Count > 0) //TODO: find most matched rule to clarify error
            {
                throw new Exception($"Syntax error: {string.Join(",", tokens.ConvertAll(x => $"{x.TokenType}({x.TokenString})"))}");
            }

            return parsedInstruction;
        }

        private static bool ProcessToken(List<Token> stack, ParsedInstruction pi)
        {
            foreach(var rule in rules)
            {
                var (pattern, handler) = rule;
                if (pattern.Count > stack.Count)
                    continue;
                for (int startPos = 0; startPos <= stack.Count - pattern.Count; startPos++)
                {
                    bool ruleMatch = true;
                    for (int i = 0; i < pattern.Count; i++)
                    {
                        if (Array.IndexOf(pattern[i], stack[startPos + i].TokenType) < 0)
                        {
                            ruleMatch = false;
                            break;
                        }
                    }
                    if (ruleMatch)
                    {
                        var matchedTokens = stack.GetRange(startPos, pattern.Count);
                        var result = handler(pi, pattern, stack, startPos);
                        if(result == null)
                            continue;
                        if(Verbose.ParserMatches)
                            Console.WriteLine($"P: {handler.Method.Name} {string.Join(",", matchedTokens.ConvertAll(x => $"{x.TokenType}({x.TokenString})"))}");
                        stack = result;
                        return true;
                    }
                }
            }
            return false;
        }

        static List<Token>? Skip(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            tokens.RemoveRange(pos, rule.Count);
            return tokens;
        }

        static List<Token>? ParseAlu(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
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

        static List<Token>? ParseAluImm(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
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

        static List<Token>? ParseRegImm(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
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
        static List<Token>? ParseLD(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
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

        static List<Token>? ParseST(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
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

        static List<Token>? ParseBranch(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
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

        static List<Token>? ParseLDI(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            int imm9 = tokens[pos + 3].TokenType == TK.NUMBER ? tokens[pos + 3].intValue : 0;
            if (tokens[pos + 3].TokenType == TK.LITERAL)
                parsedInstruction.RefLabel = tokens[pos + 3].TokenString;
            var i = new Instruction(0)
                .withOpCode(OpCode.LDI)
                .withRd(tokens[pos + 1].intValue)
                .withImm9(tokens[pos + 3].intValue);

            if (imm9 < -256 || imm9 > 255)
                throw new Exception($"Number should be -256 .. 255 for '{tokens[pos].TokenString} {tokens[pos + 1].TokenString}, {tokens[pos + 3].TokenString}'");

            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 5);
            return tokens;
        }

        //[TK.CALL, TK.JR], [TK.NUMBER, TK.LITERAL], [TK.EOL]
        static List<Token>? ParseCallJr(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            int refaddr = tokens[pos + 1].TokenType == TK.NUMBER ? tokens[pos + 1].intValue : 0;
            int rd = refaddr & 0b0000_1111;
            int imm7 = (refaddr >> 4) & 0b0111_1111;
            if (tokens[pos + 1].TokenType == TK.LITERAL)
                parsedInstruction.RefLabel = tokens[pos + 1].TokenString;

            var i = new Instruction(0)
                .withOpCode(OpCode.CALL_JMP_RET)
                .withFunc2(tokens[pos].TokenType == TK.JR ? 3 : 0)
                .withRd(rd)
                .withImm7(imm7);

            if (tokens[pos + 1].TokenType == TK.NUMBER && (refaddr < -1024 || refaddr > 1023))
                throw new Exception($"Number should be -1024 .. 1023 for '{tokens[pos].TokenString} {tokens[pos + 1].TokenString}'");

            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 3);
            return tokens;
        }

        //[TK.CALL, TK.JMP, TK.INT], [TK.REG], [TK.EOL]
        static List<Token>? ParseCallJmpInt(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var func2 = (tokens[pos].TokenType) switch
            {
                TK.CALL => 1,
                TK.JMP => 2,
                TK.INT => 0,
                _ => 0,
            };
            var i = new Instruction(0)
                .withOpCode(tokens[pos].TokenType == TK.INT ? OpCode.INT_RETI : OpCode.CALL_JMP_RET)
                .withFunc2(func2)
                .withRd(tokens[pos + 1].intValue);
            ;

            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 3);
            return tokens;
        }

        //[TK.RET, TK.RETI], [TK.EOL]
        static List<Token>? ParseRet(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(tokens[pos].TokenType == TK.RETI ? OpCode.INT_RETI : OpCode.CALL_JMP_RET)
                .withFunc2(tokens[pos].TokenType == TK.RETI ? 1 : 2)
                .withRd(tokens[pos].TokenType == TK.RETI ? 0 : 14); //R14 link register

            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 2);
            return tokens;
        }

        //[TK.MOV], [TK.REG], [TK.COMMA], [TK.EPC], [TK.EOL]
        //[TK.MOV], [TK.EPC], [TK.COMMA], [TK.REG], [TK.EOL]
        static List<Token>? ParseEPC(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(OpCode.INT_RETI)
                .withFunc2(tokens[pos + 1].TokenType == TK.REG ? 2 : 3)
                .withRd(tokens[pos+1].TokenType == TK.REG ? tokens[pos + 1].intValue : tokens[pos + 3].intValue); //R14 link register

            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 5);
            return tokens;
        }

        //[TK.NOP], [TK.EOL]
        static List<Token>? ParseNop(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            var i = new Instruction(0)
                .withOpCode(OpCode.ALU_REG_REG)
                .withFunc2((int)ALUFunc.MOV)
                .withRd(0)
                .withRs(0);

            parsedInstruction.Instructions.Add(i);
            tokens.RemoveRange(pos, 2);
            return tokens;
        }

        static List<Token>? ExpMath(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
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

        static List<Token>? ExpParen(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            Token number = tokens[pos + 1];
            tokens.RemoveRange(pos, 3);
            tokens.Insert(pos, number);
            return tokens;
        }

        static List<Token>? ExpNegative(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            if (pos > 0 && tokens[pos - 1].TokenType is TK.NUMBER or TK.RPAREN)
                return null;

            Token number = tokens[pos + 1];
            number.intValue = 0 - number.intValue;
            number.TokenString = number.intValue.ToString();
            tokens.RemoveRange(pos, 2);
            tokens.Insert(pos, number);
            return tokens;
        }

        //[TK.LITERAL], [TK.COLON]
        static List<Token>? ExpLabel(ParsedInstruction parsedInstruction, List<TK[]> rule, List<Token> tokens, int pos)
        {
            parsedInstruction.Label = tokens[pos].TokenString;
            tokens.RemoveRange(pos, 2);
            return tokens;
        }
    }
}
