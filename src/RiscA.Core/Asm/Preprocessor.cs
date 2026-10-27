using System;
using System.Collections.Generic;
using System.IO;

namespace RiscA.Core.Asm
{
    /// <summary>
    /// Source pass that runs before the parser: splices <c>.include "file"</c> and collects
    /// <c>.equ NAME, value</c> constants. Constants are substituted by
    /// <see cref="Parser.ParseLine(string, IReadOnlyDictionary{string, int})"/> wherever a
    /// number is expected, so they work in immediates, in <c>[r+off]</c> offsets and in db/dw.
    /// See doc/emulator-architecture.md, section 8.
    ///
    /// Only '.equ' and '.include' belong to this pass. '.func' opens a function entry and is
    /// not a preprocessor directive: the line stays ordinary code, the parser rule for TK.FUNC
    /// marks it (ParsedInstruction.IsFunc) and the assembler owns the entry and its alignment.
    /// </summary>
    public class Preprocessor
    {
        public const string EquDirective = ".equ";
        public const string IncludeDirective = ".include";

        //The commands of this pass are found with the same rule engine the parser runs
        //(Parser.ProcessToken), and the patterns below describe the directive grammar in full:
        //a directive line is only recognized when its whole argument matches a pattern, just
        //like an instruction is recognized by Parser.rules. Expression rules come first so a
        //value can be an arithmetic expression over numbers, the directive rules then consume
        //the finished line, and the shared skip rule at the tail drops empty lines.
        static List<(List<TK[]> pattern, RuleHandler<DirectiveMatch> handler)> directiveRules =
            [
                //Expressions - the same rules the parser runs (Asm/Expressions.cs)
                .. Expressions.Rules<DirectiveMatch>(),

                //equ: NAME, value | NAME, -value
                ([ [TK.EQU], [TK.LITERAL], [TK.COMMA], [TK.NUMBER], [TK.EOL] ], DefineEqu),
                ([ [TK.EQU], [TK.LITERAL], [TK.COMMA], [TK.MINUS, TK.NUMBER], [TK.EOL] ], DefineEqu),
                //include: "file" | 'file'
                ([ [TK.INCLUDE], [TK.STRING], [TK.EOL] ], IncludeFile),

                //a known directive that does not open its line is left to the parser, which
                //reports it as an unexpected token, so these handlers only ever fire at pos 0
                ([ [TK.EQU] ], ReportEquArgument),
                ([ [TK.INCLUDE] ], ReportIncludeArgument),
                //any other '.word' opening a line: the rules above had their chance first, so
                //this one only fires on a directive that does not exist
                ([ [TK.DOTNAME] ], UnknownDirective),

                //Skip rules
                ([ [TK.EOL] ], Expressions.Skip<DirectiveMatch>), //empty line
            ];

        //what the directive rules report about a source line: the directive it opens with and
        //the parts of its argument the patterns bound; Name stays null when the line is
        //ordinary code or a directive the full pattern did not accept
        private class DirectiveMatch
        {
            public required SrcLine Src { get; init; }
            public string? Name { get; set; }
            public Token NameToken;
            public Token ValueToken;
            public string FilePath = "";
        }

        public List<SrcLine> Lines { get { return lines; } }
        List<SrcLine> lines = [];

        public Dictionary<string, int> Constants { get { return constants; } }
        Dictionary<string, int> constants = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SrcLine> definedAt = new(StringComparer.OrdinalIgnoreCase);

        //a file is spliced in at most once per compilation, which protects both from include
        //cycles and from pulling the same library in twice through two different chains
        HashSet<string> included = new(StringComparer.OrdinalIgnoreCase);
        string rootDir = "";

        //the '.equ' lines met while splicing, in source order: the constants are only defined
        //after the whole file is spliced in, so a library standing at the end of the file still
        //provides its constants to the code above the include
        List<(SrcLine Src, Token Name, Token Value)> equLines = [];

        public void Process(string filename)
        {
            string full = Path.GetFullPath(filename);
            rootDir = Path.GetDirectoryName(full)!;
            included.Add(full);
            ExpandFile(full);
            //constants are collected after all includes are spliced in, so the order between
            //a .equ (usually coming from a library at the end of the file) and its uses does not matter
            CollectConstants();
        }

        void ExpandFile(string path)
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
                if (match.Name is null)
                {//ordinary code - including the '.func' line, which the parser recognizes and
                 //the assembler aligns - the parser of the assembler will read it
                    lines.Add(srcline);
                    continue;
                }

                //directives produce no code, but they stay in the listing;
                //ParsedInstruction is pre-set so the first pass does not try to parse them
                srcline.ParsedInstruction = new ParsedInstruction();
                lines.Add(srcline);

                if (match.Name == IncludeDirective)
                {
                    string inc = ResolveInclude(path, match.FilePath, srcline);
                    if (!included.Contains(inc))
                    {
                        included.Add(inc);
                        ExpandFile(inc);
                    }
                }
                else if (match.Name == EquDirective)
                    equLines.Add((srcline, match.NameToken, match.ValueToken));
            }
        }

        //runs the directive rules over the line and reports what they recognized: Name stays
        //null while the stack still holds tokens, which means the grammar of the directives
        //does not accept this line and it is ordinary code for the parser. A line the tokenizer
        //cannot read is left to the parser as well - it reports the broken syntax with its own
        //message, and the assembler adds the file and line number to it once
        DirectiveMatch DetectDirective(SrcLine srcline)
        {
            var match = new DirectiveMatch { Src = srcline };
            List<Token> tokens;
            try
            {
                tokens = Tokenizer.tokenizeLine(srcline.Line);
            }
            catch (Exception)
            {
                return match;
            }
            while (Parser.ProcessToken(tokens, match, directiveRules))
                ;
            if (tokens.Count != 0)
                match.Name = null;
            return match;
        }

        //directive rules: each one recognizes the directive that opens the line, binds its
        //argument and consumes the whole line - a directive never leaves tokens behind, so the
        //engine stops after the single rule that fired

        //'.equ NAME, value' accepted in full: record the directive, the name and the value are
        //taken from the bound tokens
        static List<Token>? DefineEqu(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;//a directive only ever opens its line
            Token name = tokens[pos + 1];
            match.Name = EquDirective;
            match.NameToken = name;
            match.ValueToken = tokens[pos + 3];
            tokens.Clear();
            return tokens;
        }

        static List<Token>? IncludeFile(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;
            match.Name = IncludeDirective;
            match.FilePath = tokens[pos + 1].strValue!.Trim();
            if (match.FilePath.Length == 0)
                throw new Exception(
                    $"{match.Src.Filename}:{match.Src.Pos + 1}: {IncludeDirective} expects a file name, like '.include \"lib/uart.rasm\"'");
            tokens.Clear();
            return tokens;
        }

        //reached when a '.equ' line was not consumed by the rules above: keep the old wording
        //and quote back the source text of the part that is not a number, which the token
        //patterns alone cannot say
        static List<Token>? ReportEquArgument(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;
            string where = $"{match.Src.Filename}:{match.Src.Pos + 1}";
            string rest = ArgumentText(match.Src, EquDirective);
            int comma = rest.IndexOf(',');
            string name = (comma < 0 ? rest : rest[..comma]).Trim();
            if (name.Length == 0)
                throw new Exception($"{where}: {EquDirective} expects a name, like '{EquDirective} NAME, 42'");
            if (!char.IsLetter(name[0]))
                throw new Exception($"{where}: constant name '{name}' must start with a letter");
            //a reserved word in the name position was never a LITERAL token (the tokenizer
            //turns it into MOV, REG and friends), so the full pattern above did not accept it
            if (Tokenizer.IsReserved(name))
                throw new Exception($"{where}: '{name}' is an instruction or register name, it cannot be a constant");
            string value = comma < 0 ? "" : rest[(comma + 1)..].Trim();
            throw new Exception(string.IsNullOrEmpty(value)
                ? $"{where}: constant '{name}' has no value"
                : $"{where}: value of constant '{name}' must be a number (decimal, 0x hex or 0b binary), got '{value}'");
        }

        //reached when an '.include' line was not consumed: the file name did not match the
        //quoted-string pattern, report the argument exactly as it stands in the source
        static List<Token>? ReportIncludeArgument(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;
            string where = $"{match.Src.Filename}:{match.Src.Pos + 1}";
            string rest = ArgumentText(match.Src, IncludeDirective);
            if (rest.Length == 0)
                throw new Exception($"{where}: {IncludeDirective} expects a file name, like '.include \"lib/uart.rasm\"'");
            throw new Exception($"{where}: {IncludeDirective} expects a quoted file name, got '{rest}'");
        }

        //reached only for a '.word' the rules above did not claim, because the engine tries the
        //known directives first - the name is reported exactly as it is written in the source
        static List<Token>? UnknownDirective(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;
            throw new Exception(
                $"{match.Src.Filename}:{match.Src.Pos + 1}: unknown directive '{tokens[pos].TokenString}', " +
                $"expected {EquDirective} or {IncludeDirective}");
        }

        //the source text following a directive keyword at the start of the line, comment removed:
        //kept as text because a directive quotes back whatever its grammar did not accept
        static string ArgumentText(SrcLine src, string keyword)
        {
            int at = src.Line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return "";
            int start = at + keyword.Length;
            return start >= src.Line.Length ? "" : StripComment(src.Line[start..].Trim());
        }

        string ResolveInclude(string includingFile, string path, SrcLine srcline)
        {
            string where = $"{srcline.Filename}:{srcline.Pos + 1}";
            //first the directory of the file which does the include, then the compiled file
            //directory, then the current directory - so libraries can include their own files
            var tried = new List<string>();
            foreach (string? dir in new string?[] { Path.GetDirectoryName(includingFile), rootDir })
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
            throw new Exception($"{where}: {IncludeDirective} file not found '{path}'. tried: {string.Join("; ", tried)}");
        }

        void CollectConstants()
        {
            foreach (var (sl, name, value) in equLines)
                DefineConstant(sl, name, value);
        }

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

        static string StripComment(string s)
        {
            int c = s.IndexOf(';');
            return (c < 0 ? s : s[..c]).Trim();
        }
    }
}
