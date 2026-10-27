using System;
using System.Collections.Generic;

namespace RiscA.Core.Asm
{
    /// <summary>
    /// The region check of doc/assembler-architecture.md, 7.2. It runs over the tokenized lines
    /// before any line is parsed, because the parser rules only know physical registers and would
    /// report a virtual name as a generic syntax error. A2.1 recognizes and validates named
    /// regions but does not assemble them yet; regions that use only physical registers - every
    /// source of today - pass through unchanged.
    /// </summary>
    public static class Regions
    {
        //registers the assembler owns inside a named region: r7 is the relaxation scratch,
        //R14 the link and R15 the frame pointer of the generated prologue/epilogue (doc 7.5)
        static bool IsMachineRegister(int number) => number is 7 or 14 or 15;

        public static void Check(List<SrcLine> lines, Dictionary<string, int> constants)
        {
            var region = new Region(constants);
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

        class Region(Dictionary<string, int> constants)
        {
            public SrcLine? Owner { get; private set; }
            string name = "";
            List<string> parameters = [];
            List<(SrcLine line, List<Token> tokens)> body = [];

            public void Open(SrcLine line, List<Token> tokens)
            {
                Owner = line;
                parameters.Clear();
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
                var names = new HashSet<string>(parameters, StringComparer.OrdinalIgnoreCase);
                var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
                foreach (var (line, tokens) in body)
                    Collect(line, tokens, names, labels);
                if (names.Count > 0)
                {
                    Validate();
                    foreach (var n in names)
                        if (labels.Contains(n))
                            throw Error(Owner, $"name '{n}' is also a label of the region of '{name}'");
                    //A2.2 will assemble named regions; until then they fail here, not as syntax noise
                    throw Error(Owner, $"region of '{name}': virtual names are not assembled yet");
                }
                Owner = null;
                parameters.Clear();
                body.Clear();
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
                    }
            }

            //a LITERAL becomes a virtual name unless the grammar places it in a label position:
            //a label definition, a label reference ('call x', 'j x', 'dw x', 'beqz r, x', 'ldi r, [x]')
            //or an .equ constant, which is a number to the parser
            void Collect(SrcLine line, List<Token> tokens, HashSet<string> names, HashSet<string> labels)
            {
                for (int i = 0; i < tokens.Count; i++)
                {
                    if (IsConstantBracketBase(tokens, i))
                        continue;
                    if (i + 2 < tokens.Count && tokens[i].TokenType == TK.LSBRACKET
                        && tokens[i + 1].TokenType == TK.LITERAL && tokens[i + 2].TokenType == TK.PLUS)
                    {
                        names.Add(tokens[i + 1].TokenString); //[buf + off] is a memory position
                        continue;
                    }
                    if (i + 2 < tokens.Count && tokens[i].TokenType == TK.RET
                        && tokens[i + 1].TokenType == TK.LITERAL && tokens[i + 2].TokenType == TK.EOL)
                    {
                        if (!constants.ContainsKey(tokens[i + 1].TokenString))
                            names.Add(tokens[i + 1].TokenString); //'ret x' names its exit value
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
                        names.Add(t.TokenString);
                }
            }

            //[CONST + off] in a body line: a constant as the base of a memory position,
            //which doc 7.2 lists as a name silently replaced by a number
            bool IsConstantBracketBase(List<Token> tokens, int i) =>
                i + 2 < tokens.Count && tokens[i].TokenType == TK.LSBRACKET
                && tokens[i + 1].TokenType == TK.LITERAL && tokens[i + 2].TokenType == TK.PLUS
                && constants.ContainsKey(tokens[i + 1].TokenString);

            static bool IsReferencePosition(List<Token> tokens, int i) =>
                i > 0 && (tokens[i - 1].TokenType is TK.CALL or TK.J or TK.JR or TK.DW or TK.LSBRACKET
                    || (i > 2 && tokens[i - 1].TokenType == TK.COMMA
                        && tokens[i - 2].TokenType == TK.REG
                        && tokens[i - 3].TokenType is TK.BEQZ or TK.BNEZ or TK.LA));
        }
    }
}
