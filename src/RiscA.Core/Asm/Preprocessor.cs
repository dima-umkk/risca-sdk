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
    /// </summary>
    public class Preprocessor
    {
        public const string EquDirective = ".equ";
        public const string IncludeDirective = ".include";
        //handled by the assembler itself, because .func forces function word alignment
        const string FuncDirective = ".func";

        public List<SrcLine> Lines { get { return lines; } }
        List<SrcLine> lines = [];

        public Dictionary<string, int> Constants { get { return constants; } }
        Dictionary<string, int> constants = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SrcLine> definedAt = new(StringComparer.OrdinalIgnoreCase);

        //a file is spliced in at most once per compilation, which protects both from include
        //cycles and from pulling the same library in twice through two different chains
        HashSet<string> included = new(StringComparer.OrdinalIgnoreCase);
        string rootDir = "";

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
                if (!SplitDirective(srcline.Line, out string directive, out string argument))
                {
                    lines.Add(srcline);
                    continue;
                }
                if (directive == FuncDirective)
                {//not a preprocessor directive, leave the line to the assembler
                    lines.Add(srcline);
                    continue;
                }
                if (directive != EquDirective && directive != IncludeDirective)
                    throw new Exception($"{path}:{i + 1}: unknown directive '{directive}', expected {EquDirective}, {IncludeDirective} or .func");

                //directives produce no code, but they stay in the listing;
                //ParsedInstruction is pre-set so the first pass does not try to parse them
                srcline.ParsedInstruction = new ParsedInstruction();
                lines.Add(srcline);

                if (directive == IncludeDirective)
                {
                    string inc = ResolveInclude(path, argument, srcline);
                    if (!included.Contains(inc))
                    {
                        included.Add(inc);
                        ExpandFile(inc);
                    }
                }
            }
        }

        string ResolveInclude(string includingFile, string argument, SrcLine srcline)
        {
            string path = argument;
            if (path.Length >= 2 && (path[0] == '"' || path[0] == '\'') && path[^1] == path[0])
                path = path[1..^1].Trim();
            path = StripComment(path);
            string where = $"{srcline.Filename}:{srcline.Pos + 1}";
            if (path.Length == 0)
                throw new Exception($"{where}: {IncludeDirective} expects a file name, like '.include \"lib/uart.rasm\"'");

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
            foreach (var sl in lines)
            {
                if (!SplitDirective(sl.Line, out string directive, out string argument))
                    continue;
                if (directive == EquDirective)
                    DefineConstant(sl, argument);
            }
        }

        void DefineConstant(SrcLine sl, string argument)
        {
            string where = $"{sl.Filename}:{sl.Pos + 1}";
            int p = 0;
            while (p < argument.Length && (char.IsLetterOrDigit(argument[p]) || argument[p] == '_'))
                p++;
            string name = argument[..p];
            if (name.Length == 0)
                throw new Exception($"{where}: {EquDirective} expects a name, like '{EquDirective} NAME, 42'");
            if (!char.IsLetter(name[0]))
                throw new Exception($"{where}: constant name '{name}' must start with a letter");
            if (Tokenizer.IsReserved(name))
                throw new Exception($"{where}: '{name}' is an instruction or register name, it cannot be a constant");

            string value = StripComment(argument[p..].Trim());
            while (value.StartsWith(','))
                value = StripComment(value[1..].Trim());
            if (value.Length == 0)
                throw new Exception($"{where}: constant '{name}' has no value");

            int val = ParseValue(name, value, where);
            if (constants.ContainsKey(name))
            {
                var prev = definedAt[name];
                throw new Exception($"{where}: constant '{name}' is already defined at {prev.Filename}:{prev.Pos + 1}");
            }
            constants[name] = val;
            definedAt[name] = sl;
        }

        static int ParseValue(string name, string value, string where)
        {
            var tokens = Tokenizer.tokenizeLine(value);
            int i = 0;
            bool negative = false;
            if (i < tokens.Count && tokens[i].TokenType == TK.MINUS)
            {
                negative = true;
                i++;
            }
            if (i >= tokens.Count || tokens[i].TokenType != TK.NUMBER
                || i + 1 >= tokens.Count || tokens[i + 1].TokenType != TK.EOL)
                throw new Exception($"{where}: value of constant '{name}' must be a number (decimal, 0x hex or 0b binary), got '{value}'");
            int val = tokens[i].intValue;
            return negative ? unchecked(0 - val) : val;
        }

        //splits '.name argument' into a lowercased directive name and its argument
        static bool SplitDirective(string line, out string directive, out string argument)
        {
            directive = "";
            argument = "";
            string t = line.TrimStart();
            if (t.Length == 0 || t[0] != '.')
                return false;
            int p = 1;
            while (p < t.Length && (char.IsLetterOrDigit(t[p]) || t[p] == '_'))
                p++;
            directive = t[..p].ToLowerInvariant();
            argument = StripComment(t[p..].Trim());
            return directive.Length > 1;
        }

        static string StripComment(string s)
        {
            int c = s.IndexOf(';');
            return (c < 0 ? s : s[..c]).Trim();
        }
    }
}
