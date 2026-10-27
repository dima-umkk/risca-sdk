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
                ([ [TK.INCLUDE], [TK.STRING], [TK.EOL] ], DefineInclude),

                //a directive opening a line that none of the full patterns above consumed:
                //one message naming the offending token and the two accepted forms - this pass
                //does not re-parse the argument to guess which half of it is wrong
                ([ [TK.EQU, TK.INCLUDE, TK.DOTNAME] ], ReportDirectiveError),

                //Skip rules
                ([ [TK.EOL] ], Expressions.Skip<DirectiveMatch>), //empty line
            ];

        //what the directive rules report about a source line: which directive token opens it and
        //the parts of its argument the patterns bound; Directive stays null when the line is
        //ordinary code or a directive the full pattern did not accept
        private class DirectiveMatch
        {
            public required SrcLine Src { get; init; }
            public TK? Directive { get; set; }
            public Token ConstantName;
            public Token ConstantValue;
            public string IncludePath = "";
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
            IncludeFile(full);
            //constants are collected after all includes are spliced in, so the order between
            //a .equ (usually coming from a library at the end of the file) and its uses does not matter
            CollectConstants();
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
                {//ordinary code - including the '.func' line, which the parser recognizes and
                 //the assembler aligns - the parser of the assembler will read it
                    lines.Add(srcline);
                    continue;
                }

                //directives produce no code, but they stay in the listing;
                //ParsedInstruction is pre-set so the first pass does not try to parse them
                srcline.ParsedInstruction = new ParsedInstruction();
                lines.Add(srcline);

                switch (match.Directive)
                {
                    case TK.INCLUDE:
                        string inc = FindIncludedFile(srcline, match.IncludePath);
                        //a file already standing in the listing is skipped, breaking include cycles
                        if (included.Add(inc))
                            IncludeFile(inc);
                        break;
                    case TK.EQU:
                        equLines.Add((srcline, match.ConstantName, match.ConstantValue));
                        break;
                }
            }
        }

        //runs the directive rules over the line and reports what they recognized: Directive stays
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
            match.Directive = TK.EQU;
            match.ConstantName = name;
            match.ConstantValue = tokens[pos + 3];
            tokens.Clear();
            return tokens;
        }

        //'.include "file"' accepted in full: the quoted file name goes to the include
        static List<Token>? DefineInclude(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;
            match.Directive = TK.INCLUDE;
            match.IncludePath = tokens[pos + 1].strValue!.Trim();
            tokens.Clear();
            return tokens;
        }

        //reached when a line opening with '.equ', '.include' or any other '.name' was not
        //consumed in full by the patterns above: one message with the file, the line and the
        //offending token is enough to find it, the accepted forms are named in the same breath
        static List<Token>? ReportDirectiveError(DirectiveMatch match, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0)
                return null;//a directive only ever opens its line, the parser reports the rest
            throw new Exception(
                $"{match.Src.Filename}:{match.Src.Pos + 1}: preprocessor: '{tokens[pos].TokenString}' is not accepted here, " +
                "expected '.equ NAME, value' or '.include \"file\"'");
        }

        string FindIncludedFile(SrcLine from, string path)
        {
            string where = $"{from.Filename}:{from.Pos + 1}";
            //first the directory of the file which does the include, then the compiled file
            //directory, then the current directory - so libraries can include their own files
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
    }
}
