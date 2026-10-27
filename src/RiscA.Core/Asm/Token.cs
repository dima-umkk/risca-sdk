using System;
using System.Collections.Generic;

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
        SLT,
        SLTU,
        SHL,
        SHR,
        LDI,
        LDB,
        LDW,
        STB,
        STW,
        BEQZ,
        BNEZ,
        CALL,
        RET,
        JMP,
        JR,
        //pseudo ops expanded by the parser (doc/assembler-architecture.md, 6.1)
        LI,
        LA,
        J,
        PUSH,
        POP,
        INT,
        RETI,
        EPC,
        NOP,
        DB,
        DW,
        STRING,

        //directive tokens: the tokenizer reads '.name' so that a directive is matched by the same
        //token patterns the parser uses for instructions. DOTNAME is any '.word' that is not one
        //of the known directives below; FUNC opens a function entry and is a rule of the parser,
        //not of the preprocessor - the assembler owns the entry and its alignment
        DOTNAME,
        EQU,
        INCLUDE,
        FUNC,
    }

    public record struct Token(TK TokenType, string TokenString, int TokenPos, int intValue = 0, string? strValue = null);

    public class Tokenizer
    {
        static Dictionary<string, (TK type, int number)> registerToTokenType = new()
        {
            ["r0"] = (TK.REG, 0),
            ["r1"] = (TK.REG, 1),
            ["r2"] = (TK.REG, 2),
            ["r3"] = (TK.REG, 3),
            ["r4"] = (TK.REG, 4),
            ["r5"] = (TK.REG, 5),
            ["r6"] = (TK.REG, 6),
            ["r7"] = (TK.REG, 7),
            ["r8"] = (TK.REG, 8),
            ["r9"] = (TK.REG, 9),
            ["r10"] = (TK.REG, 10),
            ["r11"] = (TK.REG, 11),
            ["r12"] = (TK.REG, 12),
            ["r13"] = (TK.REG, 13),
            ["r14"] = (TK.REG, 14),
            ["r15"] = (TK.REG, 15),
        };
        static Dictionary<string, TK> literalToTokenType = new()
        {
            ["mov"] = TK.MOV,
            ["movi"] = TK.MOVI,
            ["movl"] = TK.MOVL,

            ["add"] = TK.ADD,
            ["sub"] = TK.SUB,
            ["and"] = TK.AND,
            ["or"] = TK.OR,
            ["xor"] = TK.XOR,
            ["not"] = TK.NOT,
            ["mul"] = TK.MUL,
            ["slt"] = TK.SLT,
            ["sltu"] = TK.SLTU,
            ["shl"] = TK.SHL,
            ["shr"] = TK.SHR,

            ["ldi"] = TK.LDI,
            ["ldb"] = TK.LDB,
            ["ldw"] = TK.LDW,
            ["stb"] = TK.STB,
            ["stw"] = TK.STW,

            ["beqz"] = TK.BEQZ,
            ["bnez"] = TK.BNEZ,

            ["call"] = TK.CALL,
            ["ret"] = TK.RET,
            ["jmp"] = TK.JMP,
            ["jr"] = TK.JR,

            ["li"] = TK.LI,
            ["la"] = TK.LA,
            ["j"] = TK.J,

            ["push"] = TK.PUSH,
            ["pop"] = TK.POP,

            ["int"] = TK.INT,
            ["reti"] = TK.RETI,
            ["epc"] = TK.EPC,

            ["nop"] = TK.NOP,
            ["db"] = TK.DB,
            ["dw"] = TK.DW,
        };

        //directive names are matched as whole tokens, so '.equiv' becomes a DOTNAME the
        //preprocessor reports instead of silently treating it as code
        //the '.equ'/'.include' spellings go here as written; the Preprocessor recognizes the
        //resulting TK.EQU/TK.INCLUDE tokens by their type, not by name;
        //'.func' is not a preprocessor directive, it stays a literal here
        static Dictionary<string, TK> directiveToTokenType = new()
        {
            [".equ"] = TK.EQU,
            [".include"] = TK.INCLUDE,
            [".func"] = TK.FUNC,
        };

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') ||
            (c >= 'a' && c <= 'f') ||
            (c >= 'A' && c <= 'F');

        //true if the word is an instruction mnemonic or a register name, so it cannot be
        //used as a label or as a .equ constant name
        public static bool IsReserved(string word)
        {
            string w = word.ToLowerInvariant();
            return literalToTokenType.ContainsKey(w) || registerToTokenType.ContainsKey(w);
        }

        public static List<Token> TokenizeLine(string line)
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

                if (line[pos] == '.' && pos + 1 < line.Length && char.IsLetter(line[pos + 1])) //read a directive
                {
                    int endpos = pos + 1;
                    while (endpos < line.Length && (char.IsLetterOrDigit(line[endpos]) || line[endpos] == '_'))
                        endpos++;
                    string directive = line[pos..endpos];
                    //an unknown '.word' still becomes a token, so the preprocessor can report it
                    //by name instead of failing on the '.' as an unexpected character
                    var directiveType = directiveToTokenType.GetValueOrDefault(directive.ToLowerInvariant(), TK.DOTNAME);
                    tokens.Add(new Token(directiveType, directive, pos));
                    pos = endpos;
                    continue;
                }

                if (char.IsLetter(line[pos])) //read literal
                {
                    int endpos = pos + 1;
                    while (endpos < line.Length)
                    {
                        if (!(char.IsLetterOrDigit(line[endpos]) || line[endpos] == '_'))
                            break;
                        endpos++;
                    }
                    string tokenString = line[pos..endpos];
                    //try to check literal tokens
                    string lower = tokenString.ToLowerInvariant();
                    if (literalToTokenType.TryGetValue(lower, out var literalType))
                        tokens.Add(new Token(literalType, tokenString, pos));
                    else if (registerToTokenType.TryGetValue(lower, out var register))
                        tokens.Add(new Token(register.type, tokenString, pos, register.number));
                    else
                        tokens.Add(new Token(TK.LITERAL, tokenString, pos));
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
                                throw new Exception($"Syntax error {pos}: expected hex digits after 0x '{line[pos]}'");
                            intVal = unchecked((int)Convert.ToUInt32(line[pos..endpos], 16));
                            tokens.Add(new Token(TK.NUMBER, line[start..endpos], start, intVal));
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
                                throw new Exception($"Syntax error {pos}: expected binary digits after 0b '{line[pos]}'");
                            intVal = unchecked((int)Convert.ToUInt32(line[pos..endpos], 2));
                            tokens.Add(new Token(TK.NUMBER, line[start..endpos], start, intVal));
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
                    tokenString = line[pos..endpos];
                    if (!int.TryParse(tokenString, out intVal))
                        throw new Exception($"Syntax error {pos}: failed to parse number '{line[pos]}'");
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
                //both quote styles make a STRING token: '...' is the source of db data,
                //"..." is what a '.include "file"' argument is written with
                else if (line[pos] == '\'' || line[pos] == '"')
                {
                    char quote = line[pos];
                    int start = pos;
                    pos++;
                    int endpos = pos;
                    while (endpos < line.Length && line[endpos] != quote)
                        endpos++;
                    if (endpos >= line.Length)
                        throw new Exception($"Syntax error {pos}: unterminated string literal");
                    string content = line[pos..endpos];
                    string tokenString = line.Substring(start, endpos - start + 1);
                    tokens.Add(new Token(TK.STRING, tokenString, start, strValue: content));
                    pos = endpos + 1;
                    continue;
                }
                //no rules found - error
                throw new Exception($"Syntax error {pos}: '{line[pos]}'");
            }

            tokens.Add(new Token(TK.EOL, "{EOL}", pos));
            return tokens;
        }

    }

}
