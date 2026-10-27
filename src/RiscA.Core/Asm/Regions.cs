using System;
using System.Collections.Generic;

namespace RiscA.Core.Asm
{
    /// <summary>
    /// The region check of doc/assembler-architecture.md, 7.2. It runs over the tokenized lines
    /// before any line is parsed, because the parser rules only know physical registers and would
    /// report a virtual name as a generic syntax error. A named region is validated here and its
    /// data - parameters, names, body tokens - is handed to Frame, which lowers it to physical
    /// code (7.3); regions that use only physical registers - every source of today - pass
    /// through unchanged.
    /// </summary>
    public static class Regions
    {
        //registers the assembler owns inside a named region: r7 is the relaxation scratch,
        //r6 is borrowed as the second scratch of the A2.2 lowering until the allocator (A2.3)
        //binds names to registers, R14 the link and R15 the frame pointer of the generated
        //prologue/epilogue (doc 7.5)
        static bool IsMachineRegister(int number) => number is 6 or 7 or 14 or 15;

        public static List<RegionInfo> Check(List<SrcLine> lines, Dictionary<string, int> constants)
        {
            var named = new List<RegionInfo>();
            var region = new Region(constants, named);
            foreach (var line in lines)
            {
                if (line.ParsedInstruction is not null)
                    continue; //the .equ and .include lines are resolved by the preprocessor
                List<Token>? tokens = Tokenize(line);
                if (tokens is null || tokens.Count == 0 || tokens[0].TokenType == TK.EOL)
                    continue;
                switch (tokens[0].TokenType)
                {
                    case TK.FUNC:
                        region.Close(); //a region also ends where the next '.func' begins
                        region.Open(line, tokens);
                        break;
                    case TK.END:
                        if (region.Owner is null)
                            throw Error(line, "'.end' without a '.func'");
                        region.Close();
                        break;
                    default:
                        region.Add(line, tokens);
                        break;
                }
            }
            region.Close(); //the file ends the last region
            return named;
        }

        //a line that fails to tokenize is left to pass A, which reports it with its own context
        static List<Token>? Tokenize(SrcLine line)
        {
            try
            {
                return Tokenizer.TokenizeLine(line.Line);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static Exception Error(SrcLine line, string message) =>
            new Exception($"{line.Filename}:{line.Pos + 1}: {message}");

        //everything Frame needs to assemble one named region; the token lists stay as they came
        //from the tokenizer, Frame rewrites copies of them
        public class RegionInfo(
            SrcLine owner, string name, List<string> parameters, List<string> names,
            List<(SrcLine line, List<Token> tokens)> body)
        {
            public SrcLine Owner { get; } = owner;
            public string Name { get; } = name;
            //the '.func f(a, b)' arguments in declaration order: they arrive in r0..r5 (doc 7.1)
            public List<string> Parameters { get; } = parameters;
            //every virtual name of the region: the parameters first, the rest in order of first
            //appearance in the body, which is the frame slot order of the prologue (doc 7.3)
            public List<string> Names { get; } = names;
            public List<(SrcLine Line, List<Token> Tokens)> Body { get; } = body;
        }

        class Region(Dictionary<string, int> constants, List<RegionInfo> named)
        {
            public SrcLine? Owner { get; private set; }
            string name = "";
            List<string> parameters = [];
            List<string> names = [];
            HashSet<string> labels = new(StringComparer.OrdinalIgnoreCase);
            List<(SrcLine line, List<Token> tokens)> body = [];

            public void Open(SrcLine line, List<Token> tokens)
            {
                Owner = line;
                parameters.Clear();
                names.Clear();
                labels.Clear();
                body.Clear();
                int i = 1;
                if (i >= tokens.Count || tokens[i].TokenType != TK.LITERAL)
                    throw Error(line, "'.func' expects a name");
                name = tokens[i].TokenString;
                i++;
                if (i < tokens.Count && tokens[i].TokenType == TK.COLON)
                {
                    i++;
                    if (i < tokens.Count && tokens[i].TokenType == TK.LPAREN)
                        throw Error(line, $"parameters follow '{name}' directly, without ':'");
                }
                if (i < tokens.Count && tokens[i].TokenType == TK.LPAREN)
                    ReadParameters(line, tokens, ref i);
            }

            void ReadParameters(SrcLine line, List<Token> tokens, ref int i)
            {
                i++;
                while (i < tokens.Count && tokens[i].TokenType != TK.RPAREN && tokens[i].TokenType != TK.EOL)
                {
                    if (tokens[i].TokenType == TK.REG)
                        throw Error(line, $"'{tokens[i].TokenString}' is a register, it cannot be a parameter");
                    if (Tokenizer.IsReserved(tokens[i].TokenString))
                        throw Error(line, $"parameter '{tokens[i].TokenString}' is a mnemonic");
                    if (tokens[i].TokenType != TK.LITERAL)
                        throw Error(line, $"'{tokens[i].TokenString}' is not a parameter name");
                    CheckParameter(line, tokens[i].TokenString);
                    i++;
                    if (i < tokens.Count && tokens[i].TokenType == TK.COMMA)
                        i++;
                }
                if (i >= tokens.Count || tokens[i].TokenType != TK.RPAREN)
                    throw Error(line, $"the parameter list of '{name}' has no ')'");
                if (i + 1 < tokens.Count && tokens[i + 1].TokenType != TK.EOL)
                    throw Error(line, $"unexpected text after the parameter list of '.func {name}'");
                if (parameters.Count > 6)
                    throw Error(line, $"'{name}' has {parameters.Count} parameters, r0..r5 carry at most 6");
            }

            void CheckParameter(SrcLine line, string parameter)
            {
                if (constants.ContainsKey(parameter))
                    throw Error(line, $"parameter '{parameter}' is already an .equ constant");
                if (parameter.Equals(name, StringComparison.OrdinalIgnoreCase))
                    throw Error(line, $"parameter '{parameter}' repeats the function name");
                if (parameters.Exists(p => p.Equals(parameter, StringComparison.OrdinalIgnoreCase)))
                    throw Error(line, $"parameter '{parameter}' is listed twice");
                parameters.Add(parameter);
            }

            public void Add(SrcLine line, List<Token> tokens)
            {
                if (Owner is not null)
                    body.Add((line, tokens));
            }

            public void Close()
            {
                if (Owner is null)
                    return;
                labels.Add(name);
                names.AddRange(parameters); //the parameters are the first names, in declaration order
                foreach (var (line, tokens) in body)
                    Collect(line, tokens);
                if (names.Count > 0)
                {
                    Validate();
                    foreach (var n in names)
                        if (labels.Contains(n))
                            throw Error(Owner, $"name '{n}' is also a label of the region of '{name}'");
                    named.Add(new RegionInfo(Owner, name, [.. parameters], [.. names], [.. body]));
                }
                Owner = null;
            }

            //the prohibitions of doc 7.2 and 7.5 checked in the region body itself
            void Validate()
            {
                foreach (var (line, tokens) in body)
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        Token t = tokens[i];
                        if (t.TokenType == TK.REG && IsMachineRegister(t.intValue))
                            throw Error(line, $"r{t.intValue} is not allowed in a named region of '{name}': the assembler owns it");
                        if (t.TokenType is TK.PUSH or TK.POP)
                            throw Error(line, $"'{t.TokenString}' is not allowed in a named region of '{name}': R15 is the frame pointer");
                        if (IsConstantBracketBase(tokens, i))
                            throw Error(line, $"'{tokens[i + 1].TokenString}' is an .equ constant, not a frame slot of '{name}'");
                        if (t.TokenType is TK.LI or TK.LA)
                        {
                            if (i + 1 < tokens.Count && IsName(tokens[i + 1].TokenString))
                                throw Error(line, $"'{t.TokenString}' with virtual name '{tokens[i + 1].TokenString}' is assembled after register allocation (A2.3)");
                            if (i + 3 < tokens.Count && tokens[i + 2].TokenType == TK.COMMA && IsName(tokens[i + 3].TokenString))
                                throw Error(line, $"'{t.TokenString}' with virtual name '{tokens[i + 3].TokenString}' is assembled after register allocation (A2.3)");
                        }
                    }
            }

            //a LITERAL becomes a virtual name unless the grammar places it in a label position:
            //a label definition, a label reference ('call x', 'j x', 'dw x', 'beqz r, x', 'ldi r, [x]')
            //or an .equ constant, which is a number to the parser
            void Collect(SrcLine line, List<Token> tokens)
            {
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (IsConstantBracketBase(tokens, i))
                        continue;
                    if (i + 2 < tokens.Count && tokens[i].TokenType == TK.LSBRACKET
                        && tokens[i + 1].TokenType == TK.LITERAL && tokens[i + 2].TokenType == TK.PLUS)
                    {
                        AddName(tokens[i + 1].TokenString); //[buf + off] is a memory position
                        continue;
                    }
                    if (i + 2 < tokens.Count && tokens[i].TokenType == TK.RET
                        && tokens[i + 1].TokenType == TK.LITERAL && tokens[i + 2].TokenType == TK.EOL)
                    {
                        if (!constants.ContainsKey(tokens[i + 1].TokenString))
                            AddName(tokens[i + 1].TokenString); //'ret x' names its exit value
                        continue;
                    }
                    Token t = tokens[i];
                    if (t.TokenType != TK.LITERAL || IsReferencePosition(tokens, i))
                        continue;
                    if (i + 1 < tokens.Count && tokens[i + 1].TokenType == TK.COLON)
                    {
                        labels.Add(t.TokenString);
                        continue;
                    }
                    if (!constants.ContainsKey(t.TokenString))
                        AddName(t.TokenString);
                }
            }

            void AddName(string tokenString)
            {
                if (!IsName(tokenString))
                    names.Add(tokenString);
            }

            bool IsName(string tokenString) =>
                names.Exists(n => n.Equals(tokenString, StringComparison.OrdinalIgnoreCase));

            //[CONST + off] in a body line: a constant as the base of a memory position,
            //which doc 7.2 lists as a name silently replaced by a number
            bool IsConstantBracketBase(List<Token> tokens, int i) =>
                i + 2 < tokens.Count && tokens[i].TokenType == TK.LSBRACKET
                && tokens[i + 1].TokenType == TK.LITERAL && tokens[i + 2].TokenType == TK.PLUS
                && constants.ContainsKey(tokens[i + 1].TokenString);

            static bool IsReferencePosition(List<Token> tokens, int i) =>
                i > 0 && (tokens[i - 1].TokenType is TK.CALL or TK.J or TK.JR or TK.DW or TK.LSBRACKET
                    || (i > 2 && tokens[i - 1].TokenType == TK.COMMA
                        && tokens[i - 2].TokenType is TK.REG or TK.LITERAL
                        && tokens[i - 3].TokenType is TK.BEQZ or TK.BNEZ or TK.LA));
        }
    }
}
