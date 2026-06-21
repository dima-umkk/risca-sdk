using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RiscA.Core.Asm
{
    public enum TK
    {
        //base tokens
        LITERAL,     // abcdef_123
        NUMBER,      // 12345
        COMMA,       // ,
        COLON,       // :
        LSBRACKET,   // [
        RSBRACKET,   // ]
        LPAREN,      // (
        RPAREN,      // )
        PLUS,        // +
        MINUS,       // -
        ASTER,       // *
        SLASH,       // /
        EOL,         //End of line

        //literal tokens
        REG,
        MOV,
        MOVI,
        MOVL,
        ADD,
        SUB,
        AND,
        OR,
        XOR,
        NOT,
        MUL,
        SHL,
        SHR,
        LDI,
        LDB,
        LDW,
        STB,
        STW,
        BEQZ,
        BNEZ,
        BGTZ,
        BLTZ,
        CALL,
        RET,
        JMP,
        JR,
        INT,
        RETI,
        EPC,
    }

    public record struct Token(TK TokenType, string TokenString, int TokenPos, int intValue = 0, string? strValue = null);

    public class Tokenizer
    {
        static Dictionary<string, (TK, int)> registerToTokenType = new Dictionary<string, (TK, int)>
        {
            {"r0", (TK.REG, 0) },
            {"r1", (TK.REG, 1) },
            {"r2", (TK.REG, 2) },
            {"r3", (TK.REG, 3) },
            {"r4", (TK.REG, 4) },
            {"r5", (TK.REG, 5) },
            {"r6", (TK.REG, 6) },
            {"r7", (TK.REG, 7) },
            {"r8", (TK.REG, 8) },
            {"r9", (TK.REG, 9) },
            {"r10", (TK.REG, 10) },
            {"r11", (TK.REG, 11) },
            {"r12", (TK.REG, 12) },
            {"r13", (TK.REG, 13) },
            {"r14", (TK.REG, 14) },
            {"r15", (TK.REG, 15) },
        };
        static Dictionary<string, TK> literalToTokenType = new Dictionary<string, TK> 
        {
            {"mov", TK.MOV},
            {"movi", TK.MOVI},
            {"movl", TK.MOVL},

            {"add", TK.ADD},
            {"sub", TK.SUB},
            {"and", TK.AND},
            {"or", TK.OR},
            {"xor", TK.XOR},
            {"not", TK.NOT},
            {"mul", TK.MUL},
            {"shl", TK.SHL},
            {"shr", TK.SHR},

            {"ldi", TK.LDI},
            {"ldb", TK.LDB},
            {"ldw", TK.LDW},
            {"stb", TK.STB},
            {"stw", TK.STW},

            {"beqz", TK.BEQZ },
            {"bnez", TK.BNEZ },
            {"bgtz", TK.BGTZ },
            {"bltz", TK.BLTZ },

            {"call", TK.CALL },
            {"ret", TK.RET },
            {"jmp", TK.JMP },
            {"jr", TK.JR },

            {"int", TK.INT },
            {"reti", TK.RETI },
            {"epc", TK.EPC },
        };

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') ||
            (c >= 'a' && c <= 'f') ||
            (c >= 'A' && c <= 'F');

        public static List<Token> tokenizeLine(string filename, string line, int linenumber)
        {
            var tokens = new List<Token>();
            int pos = 0;
            while (pos < line.Length)
            {
                if (char.IsWhiteSpace(line[pos])) //skip whitespaces
                {
                    pos++;
                    continue;
                }
                if (line[pos] == ';') // comment - skip rest of the line
                    break;

                if (char.IsLetter(line[pos])) //read literal
                {
                    int endpos = pos + 1;
                    while (endpos < line.Length)
                    {
                        if (!char.IsLetterOrDigit(line[endpos]))
                            break;
                        endpos++;
                    }
                    string tokenString = line.Substring(pos, endpos - pos);
                    //try to check literal tokens
                    if (literalToTokenType.ContainsKey(tokenString.ToLower()))
                    {
                        var tokenType = literalToTokenType[tokenString.ToLower()];
                        tokens.Add(new Token(tokenType, tokenString, pos));
                    }
                    else if(registerToTokenType.ContainsKey(tokenString.ToLower()))
                    {
                        var tokenType = registerToTokenType[tokenString.ToLower()].Item1;
                        var regNumber = registerToTokenType[tokenString.ToLower()].Item2;
                        tokens.Add(new Token(tokenType, tokenString, pos, regNumber));
                    }
                    else
                    {
                        tokens.Add(new Token(TK.LITERAL, tokenString, pos));
                    }
                    pos = endpos;
                    continue;
                }
                else if (char.IsDigit(line[pos])) //read number
                {
                    int endpos;
                    string tokenString;
                    int intVal;

                    if (line[pos] == '0' && pos + 1 < line.Length)
                    {
                        char nextLower = char.ToLowerInvariant(line[pos + 1]);
                        if (nextLower == 'x') // hex
                        {
                            int start = pos;
                            pos += 2;
                            endpos = pos;
                            while (endpos < line.Length && IsHexDigit(line[endpos]))
                                endpos++;
                            if (endpos == pos)
                                throw new Exception($"Syntax error: expected hex digits after 0x: {filename}: {linenumber}:{pos}");
                            intVal = unchecked((int)Convert.ToUInt32(line.Substring(pos, endpos - pos), 16));
                            tokens.Add(new Token(TK.NUMBER, line.Substring(start, endpos - start), start, intVal));
                            pos = endpos;
                            continue;
                        }
                        if (nextLower == 'b') // binary
                        {
                            int start = pos;
                            pos += 2;
                            endpos = pos;
                            while (endpos < line.Length && (line[endpos] == '0' || line[endpos] == '1'))
                                endpos++;
                            if (endpos == pos)
                                throw new Exception($"Syntax error: expected binary digits after 0b: {filename}: {linenumber}:{pos}");
                            intVal = unchecked((int)Convert.ToUInt32(line.Substring(pos, endpos - pos), 2));
                            tokens.Add(new Token(TK.NUMBER, line.Substring(start, endpos - start), start, intVal));
                            pos = endpos;
                            continue;
                        }
                    }

                    // decimal
                    endpos = pos + 1;
                    while (endpos < line.Length)
                    {
                        if (!char.IsDigit(line[endpos]))
                            break;
                        endpos++;
                    }
                    tokenString = line.Substring(pos, endpos - pos);
                    if (!int.TryParse(tokenString, out intVal))
                        throw new Exception($"Syntax error: failed to parse number: {filename}: {linenumber}:{pos}");
                    tokens.Add(new Token(TK.NUMBER, tokenString, pos, intVal));
                    pos = endpos;
                    continue;
                }
                else if (line[pos] == ',')
                {
                    tokens.Add(new Token(TK.COMMA, ",", pos));
                    pos++;
                    continue;
                }
                else if (line[pos] == '[')
                {
                    tokens.Add(new Token(TK.LSBRACKET, "[", pos));
                    pos++;
                    continue;
                }
                else if (line[pos] == ']')
                {
                    tokens.Add(new Token(TK.RSBRACKET, "]", pos));
                    pos++;
                    continue;
                }
                else if (line[pos] == '(')
                {
                    tokens.Add(new Token(TK.LPAREN, "(", pos));
                    pos++;
                    continue;
                }
                else if (line[pos] == ')')
                {
                    tokens.Add(new Token(TK.RPAREN, ")", pos));
                    pos++;
                    continue;
                }
                else if (line[pos] == '+')
                {
                    tokens.Add(new Token(TK.PLUS, "+", pos));
                    pos++;
                    continue;
                }
                else if (line[pos] == '-')
                {
                    tokens.Add(new Token(TK.MINUS, "-", pos));
                    pos++;
                    continue;
                }
                else if (line[pos] == '*')
                {
                    tokens.Add(new Token(TK.ASTER, "*", pos));
                    pos++;
                    continue;
                }
                else if (line[pos] == '/')
                {
                    tokens.Add(new Token(TK.SLASH, "/", pos));
                    pos++;
                    continue;
                }
                else if (line[pos] == ':')
                {
                    tokens.Add(new Token(TK.COLON, ":", pos));
                    pos++;
                    continue;
                }
                //no rules found - error
                throw new Exception($"Syntax error '{line[pos]}' {filename}: {linenumber}:{pos}");
            }

            tokens.Add(new Token(TK.EOL, "{EOL}", pos));
            return tokens;
        }

    }

}
