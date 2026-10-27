using System;
using System.Collections.Generic;
using System.IO;

namespace RiscA.Core.Asm
{
    /// <summary>Source pass before the parser: splices <c>.include</c> and defines <c>.equ</c>
    /// constants, which the parser substitutes later.</summary>
    public class Preprocessor
    {
        //the same rule engine as the parser: a directive counts only when its whole line matches
        static List<(List<TK[]> pattern, RuleHandler<DirectiveMatch> handler)> directiveRules =
            [
                .. Expressions.Rules<DirectiveMatch>(),

                //equ: NAME, value | NAME, -value
                ([ [TK.EQU], [TK.LITERAL], [TK.COMMA], [TK.NUMBER], [TK.EOL] ], DefineEqu),
                ([ [TK.EQU], [TK.LITERAL], [TK.COMMA], [TK.MINUS, TK.NUMBER], [TK.EOL] ], DefineEqu),
                ([ [TK.INCLUDE], [TK.STRING], [TK.EOL] ], DefineInclude),

                //a '.equ'/'.include'/'.name' that no full pattern above consumed
                ([ [TK.EQU, TK.INCLUDE, TK.DOTNAME] ], ReportDirectiveError),

                ([ [TK.EOL] ], Expressions.Skip<DirectiveMatch>), //empty line
            ];

        //a line the directive rules recognized; Directive is null when it is ordinary code
        private class DirectiveMatch
        {
            public required SrcLine Src { get; init; }
            public TK? Directive { get; set; }
            public Token ConstantName;
            public Token ConstantValue;
            public string IncludePath = "";
        }

        public List<SrcLine> Lines => lines;
        List<SrcLine> lines = [];

        public Dictionary<string, int> Constants => constants;
        Dictionary<string, int> constants = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SrcLine> definedAt = new(StringComparer.OrdinalIgnoreCase);

        //each file is spliced in at most once, which also breaks include cycles
        HashSet<string> included = new(StringComparer.OrdinalIgnoreCase);
        string rootDir = "";

        public void Process(string filename)
        {
            string full = Path.GetFullPath(filename);
            rootDir = Path.GetDirectoryName(full)!;
            included.Add(full);
            IncludeFile(full);
        }

        void IncludeFile(string path)
        {
            string[] text;
            try
            {
                text = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                throw new Exception($"{path}: cannot read source file: {ex.Message}");
            }

            for (int i = 0; i < text.Length; i++)
            {
                var srcline = new SrcLine(text[i], i, path);
                var match = DetectDirective(srcline);
                if (match.Directive is null)
                {//ordinary code, including '.func', is read by the parser
                    lines.Add(srcline);
                    continue;
                }

                //directives stay in the listing but pass A must not parse them
                srcline.ParsedInstruction = new ParsedInstruction();
                lines.Add(srcline);

                switch (match.Directive)
                {
                    case TK.INCLUDE:
                        string inc = FindIncludedFile(srcline, match.IncludePath);
                        if (included.Add(inc))
                            IncludeFile(inc);
                        break;
                    case TK.EQU:
                        DefineConstant(srcline, match.ConstantName, match.ConstantValue);
                        break;
                }
            }
        }

        //leftover tokens or an unreadable line mean the grammar rejects it: ordinary code
        DirectiveMatch DetectDirective(SrcLine srcline)
        {
            var match = new DirectiveMatch { Src = srcline };
            List<Token> tokens;
            try
            {
                tokens = Tokenizer.TokenizeLine(srcline.Line);
            }
            catch (Exception)
            {
                return match;
            }
            while (Parser.ProcessToken(tokens, match, directiveRules))
                ;
            if (tokens.Count != 0)
                match.Directive = null;
            return match;
        }

        static List<Token>? DefineEqu(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;//a directive only ever opens its line
            Token name = tokens[pos + 1];
            match.Directive = TK.EQU;
            match.ConstantName = name;
            match.ConstantValue = tokens[pos + 3];
            tokens.Clear();
            return tokens;
        }

        static List<Token>? DefineInclude(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;
            match.Directive = TK.INCLUDE;
            match.IncludePath = tokens[pos + 1].strValue!.Trim();
            tokens.Clear();
            return tokens;
        }

        static List<Token>? ReportDirectiveError(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;
            throw new Exception(
                $"{match.Src.Filename}:{match.Src.Pos + 1}: preprocessor: '{tokens[pos].TokenString}' is not accepted here, " +
                "expected '.equ NAME, value' or '.include \"file\"'");
        }

        string FindIncludedFile(SrcLine from, string path)
        {
            string where = $"{from.Filename}:{from.Pos + 1}";
            //first next to the including file, then the root directory, then the cwd
            var tried = new List<string>();
            foreach (string? dir in new string?[] { Path.GetDirectoryName(from.Filename), rootDir })
            {
                if (string.IsNullOrEmpty(dir))
                    continue;
                string candidate = Path.GetFullPath(Path.Combine(dir, path));
                if (!tried.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    tried.Add(candidate);
                if (File.Exists(candidate))
                    return candidate;
            }
            string cwd = Path.GetFullPath(path);
            tried.Add(cwd);
            if (File.Exists(cwd))
                return cwd;
            throw new Exception($"{where}: .include file not found '{path}'. tried: {string.Join("; ", tried)}");
        }

        //the parser substitutes constants after this pass, so a use may precede the definition
        void DefineConstant(SrcLine sl, Token name, Token value)
        {
            string where = $"{sl.Filename}:{sl.Pos + 1}";
            if (constants.ContainsKey(name.TokenString))
            {
                var prev = definedAt[name.TokenString];
                throw new Exception($"{where}: constant '{name.TokenString}' is already defined at {prev.Filename}:{prev.Pos + 1}");
            }
            constants[name.TokenString] = value.intValue;
            definedAt[name.TokenString] = sl;
        }
    }
}
