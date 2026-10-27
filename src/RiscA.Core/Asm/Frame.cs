using System;
using System.Collections.Generic;

namespace RiscA.Core.Asm
{
    /// <summary>
    /// The frame lowering of doc/assembler-architecture.md, 7.3 (milestone A2.2): in a named
    /// region every virtual name gets one 4-byte frame slot, the '.func' line grows a prologue
    /// ('SUB R15', the saves, the parameters copied from their arrival registers r0..r5), every
    /// body line is rewritten to physical tokens and parsed by the ordinary rules, and one
    /// epilogue (label '@retN', inserted after the last body line) restores the saves, releases
    /// the frame and returns over R14; every 'ret x' moves its value to r0 and jumps there.
    /// This is the interim form without an allocator, which A2.3 replaces: every name lives in
    /// its slot, so a frame is limited to 7 slots, the word-offset range of LDW/STW, and r6/r7
    /// are scratches borrowed by the lowering and forbidden in the region body.
    /// </summary>
    public static class Frame
    {
        const int Result = 0;
        const int SecondScratch = 6;
        const int Scratch = 7;
        const int Link = 14;
        const int StackPointer = 15;
        const int MaxFrameSlots = 7;

        //mnemonics that write their first operand: the positions where a name becomes a value
        static readonly TK[] definitionOps =
            [TK.MOV, TK.ADD, TK.SUB, TK.AND, TK.OR, TK.XOR, TK.NOT, TK.MUL, TK.SLT, TK.SLTU,
             TK.SHL, TK.SHR, TK.MOVI, TK.MOVL, TK.LDW, TK.LDB];
        static readonly TK[] aluOps =
            [TK.MOV, TK.ADD, TK.SUB, TK.AND, TK.OR, TK.XOR, TK.NOT, TK.MUL, TK.SLT, TK.SLTU, TK.SHL, TK.SHR];

        public static void Build(List<SrcLine> src, List<Regions.RegionInfo> regions, Dictionary<string, int> constants)
        {
            int epilogue = 0;
            foreach (var region in regions)
                BuildRegion(src, region, constants, $"@ret{++epilogue}");
        }

        static void BuildRegion(List<SrcLine> src, Regions.RegionInfo region, Dictionary<string, int> constants, string retLabel)
        {
            var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in region.Names)
                slots[n] = slots.Count;

            var saves = new List<(int reg, int slot)>();
            if (region.Body.Any(b => b.Tokens.Any(t => t.TokenType == TK.CALL)))
            {
                //the body of a non-leaf region calls, and a call writes R14: the return address of
                //the region itself goes into the frame with the r8..r13 the body clobbers (doc 7.3)
                saves.Add((Link, slots.Count));
                foreach (int r in ClobberedRegisters(region))
                    saves.Add((r, slots.Count + saves.Count));
            }
            int total = slots.Count + saves.Count;
            if (total > MaxFrameSlots)
                throw Error(region.Owner, $"the frame of '{region.Name}' needs {total} slots, its names plus the saved registers, at most {MaxFrameSlots} fit into the word offsets of LDW/STW");

            //the rule loop of the parser consumes rules in table order, so a token stream of
            //several statements comes out scrambled: every synthesized statement goes to its
            //own line and is parsed as an ordinary one-statement line
            region.Owner.ParsedInstruction = Parse(region.Owner, Tokenizer.TokenizeLine(region.Owner.Line), constants);
            var prologue = new List<List<Token>>();
            AddAdjust(prologue, TK.SUB, total * 4);
            foreach (var (reg, slot) in saves)
                AddSlotAccess(prologue, TK.STW, slot, reg);
            //the parameters arrive in r0..r5 by position (doc 7.1)
            for (int p = 0; p < region.Parameters.Count; p++)
                AddSlotAccess(prologue, TK.STW, slots[region.Parameters[p]], p);
            int after = InsertStatements(src, src.IndexOf(region.Owner) + 1, prologue, region.Owner, constants);

            var lowering = new Lowering(slots, ValueDefs(region, slots), retLabel);
            foreach (var (line, tokens) in region.Body)
            {
                var statements = lowering.Groups(tokens);
                if (statements.Count == 0)
                    continue;
                line.ParsedInstruction = Parse(line, statements[0], constants);
                after = InsertStatements(src, src.IndexOf(line) + 1, statements.GetRange(1, statements.Count - 1), line, constants);
            }

            //one epilogue per region, its saves never touch r0 which already carries the exit value;
            //it lands after the last body line, before the constant pool that pass B places before
            //the next '.func'
            var epilogue = new List<List<Token>> { new List<Token> { Literal(retLabel), Kind(TK.COLON, ":"), Eol } };
            foreach (var (reg, slot) in saves)
                AddSlotAccess(epilogue, TK.LDW, slot, reg);
            AddAdjust(epilogue, TK.ADD, total * 4);
            epilogue.Add([Kind(TK.RET, "ret"), Eol]);
            after = InsertStatements(src, after, new List<List<Token>> { epilogue[0] }, region.Owner, constants, $"\t{retLabel}: ;epilogue of '{region.Name}'");
            InsertStatements(src, after, epilogue.GetRange(1, epilogue.Count - 1), region.Owner, constants, $"\t;{retLabel}");
        }

        static List<int> ClobberedRegisters(Regions.RegionInfo region)
        {
            var written = new SortedSet<int>();
            foreach (var (_, tokens) in region.Body)
                for (int i = 0; i + 1 < tokens.Count; i++)
                    if (definitionOps.Contains(tokens[i].TokenType)
                        && tokens[i + 1].TokenType == TK.REG && tokens[i + 1].intValue is >= 8 and <= 13)
                        written.Add(tokens[i + 1].intValue);
            return [.. written];
        }

        //names written somewhere in the body hold a value, so [name + off] reads through it
        //(the name as a pointer) instead of addressing its own slot (doc 7.3)
        static HashSet<string> ValueDefs(Regions.RegionInfo region, Dictionary<string, int> slots)
        {
            var defs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, tokens) in region.Body)
                for (int i = 0; i + 2 < tokens.Count; i++)
                    if (definitionOps.Contains(tokens[i].TokenType) && tokens[i + 2].TokenType == TK.COMMA
                        && tokens[i + 1].TokenType == TK.LITERAL && slots.ContainsKey(tokens[i + 1].TokenString))
                        defs.Add(tokens[i + 1].TokenString);
            return defs;
        }

        //parses the statements one per line into synthesized SrcLines and returns the position
        //just behind the inserted block, so the next block can follow the previous one
        static int InsertStatements(List<SrcLine> src, int at, List<List<Token>> statements, SrcLine template, Dictionary<string, int> constants, string? lineText = null)
        {
            foreach (var tokens in statements)
            {
                var line = new SrcLine(lineText ?? $"\t;{template.Line.Trim()}", template.Pos, template.Filename);
                line.ParsedInstruction = Parse(line, tokens, constants);
                src.Insert(at++, line);
            }
            return at;
        }

        static ParsedInstruction Parse(SrcLine line, List<Token> tokens, Dictionary<string, int> constants)
        {
            try
            {
                return Parser.ParseTokens(tokens, constants);
            }
            catch (Exception ex)
            {
                throw Error(line, ex.Message);
            }
        }

        static Exception Error(SrcLine line, string message) =>
            new Exception($"{line.Filename}:{line.Pos + 1}: {message}");

        static readonly Token Eol = new(TK.EOL, "{EOL}", 0);

        static Token Kind(TK kind, string text) => new(kind, text, 0);
        static Token Kind(TK kind) => new(kind, kind.ToString().ToLowerInvariant(), 0);
        static Token Literal(string text) => new(TK.LITERAL, text, 0);
        static Token Reg(int number) => new(TK.REG, $"r{number}", 0, number);
        static Token Num(int value) => new(TK.NUMBER, value.ToString(), 0, value);
        static Token Comma => new(TK.COMMA, ",", 0);

        static void AddAdjust(List<List<Token>> statements, TK op, int bytes) =>
            statements.Add([Kind(op), Reg(StackPointer), Comma, Num(bytes), Eol]);

        //stw/ldw of a frame slot: [r15 + word offset] is the grammar of the offset, so the slot
        //index is the number the author would have written (doc RiscA.MD: the word offset steps by 4)
        static void AddSlotAccess(List<List<Token>> statements, TK op, int slot, int reg)
        {
            List<Token> bracket = [Kind(TK.LSBRACKET, "["), Reg(StackPointer), Kind(TK.PLUS, "+"), Num(slot), Kind(TK.RSBRACKET, "]")];
            if (op == TK.STW)
                statements.Add([Kind(op), ..bracket, Comma, Reg(reg), Eol]);
            else
                statements.Add([Kind(op), Reg(reg), Comma, ..bracket, Eol]);
        }

        //rewrites one body line into physical statements: the virtual names become slot accesses
        //over the borrowed scratches r7, then r6; a statement the shapes below do not cover is
        //left to the parser, which reports it as an ordinary syntax error
        class Lowering(Dictionary<string, int> slots, HashSet<string> valueDefs, string retLabel)
        {
            public List<List<Token>> Groups(List<Token> tokens)
            {
                var statements = new List<List<Token>>();
                int i = 0;
                if (i + 1 < tokens.Count && tokens[i].TokenType == TK.LITERAL && tokens[i + 1].TokenType == TK.COLON)
                {
                    statements.Add([tokens[i], tokens[i + 1], Eol]);
                    i += 2;
                }
                if (i < tokens.Count && !Statement(tokens, ref i, statements))
                    statements.Add(tokens.GetRange(i, tokens.Count - i));
                return statements;
            }

            bool Statement(List<Token> t, ref int i, List<List<Token>> o) => t[i].TokenType switch
            {
                TK.EOL => Skip(t, ref i, o),
                TK.RET => Ret(t, ref i, o),
                TK.BEQZ or TK.BNEZ => Branch(t, ref i, o),
                TK.LDW or TK.LDB => Load(t, ref i, o),
                TK.STW or TK.STB => Store(t, ref i, o),
                TK.MOV => Mov(t, ref i, o),
                _ => aluOps.Contains(t[i].TokenType) && Alu(t, ref i, o),
            };

            //a label line ends right after its 'name:'; the lone EOL is not a statement
            bool Skip(List<Token> t, ref int i, List<List<Token>> o)
            {
                i++;
                return true;
            }

            //'ret' jumps to the single epilogue; 'ret reg'/'ret name' first move the exit value to r0
            bool Ret(List<Token> t, ref int i, List<List<Token>> o)
            {
                Token x = i + 1 < t.Count ? t[i + 1] : default;
                if (x.TokenType == TK.EOL)
                {
                    Jump(o);
                    i += 2;
                    return true;
                }
                if (i + 2 >= t.Count || t[i + 2].TokenType != TK.EOL)
                    return false;
                if (x.TokenType == TK.REG)
                {
                    if (x.intValue != Result)
                        AddRegMove(o, Result, x.intValue);
                    Jump(o);
                    i += 3;
                    return true;
                }
                if (IsName(x))
                {
                    AddSlotAccess(o, TK.LDW, Slot(x), Result);
                    Jump(o);
                    i += 3;
                    return true;
                }
                return false;
            }

            //'beqz name, target' reads its operand from the slot through r7
            bool Branch(List<Token> t, ref int i, List<List<Token>> o)
            {
                if (i + 4 >= t.Count || !IsName(t[i + 1]) || t[i + 2].TokenType != TK.COMMA || t[i + 4].TokenType != TK.EOL)
                    return false;
                AddSlotAccess(o, TK.LDW, Slot(t[i + 1]), Scratch);
                o.Add([Kind(t[i].TokenType, t[i].TokenString), Reg(Scratch), Comma, t[i + 3], Eol]);
                i += 5;
                return true;
            }

            bool Load(List<Token> t, ref int i, List<List<Token>> o)
            {
                if (i + 8 >= t.Count || t[i + 2].TokenType != TK.COMMA || t[i + 3].TokenType != TK.LSBRACKET
                    || t[i + 5].TokenType != TK.PLUS || t[i + 6].TokenType != TK.NUMBER
                    || t[i + 7].TokenType != TK.RSBRACKET || t[i + 8].TokenType != TK.EOL)
                    return false;
                bool destIsName = IsName(t[i + 1]);
                if (!destIsName && t[i + 1].TokenType != TK.REG)
                    return false;
                bool baseIsName = IsName(t[i + 4]);
                if (!baseIsName && t[i + 4].TokenType != TK.REG)
                    return false;
                if (!destIsName && !baseIsName)
                    return false;
                List<Token> bracket = Bracket(t[i].TokenType, t[i + 4], t[i + 6].intValue, false, o);
                int rd = destIsName ? Scratch : t[i + 1].intValue;
                o.Add([Kind(t[i].TokenType, t[i].TokenString), Reg(rd), Comma, Kind(TK.LSBRACKET, "["), ..bracket, Kind(TK.RSBRACKET, "]"), Eol]);
                if (destIsName)
                    AddSlotAccess(o, TK.STW, Slot(t[i + 1]), Scratch);
                i += 9;
                return true;
            }

            bool Store(List<Token> t, ref int i, List<List<Token>> o)
            {
                if (i + 8 >= t.Count || t[i + 1].TokenType != TK.LSBRACKET || t[i + 3].TokenType != TK.PLUS
                    || t[i + 4].TokenType != TK.NUMBER || t[i + 5].TokenType != TK.RSBRACKET
                    || t[i + 6].TokenType != TK.COMMA || t[i + 8].TokenType != TK.EOL)
                    return false;
                bool srcIsName = IsName(t[i + 7]);
                bool baseIsName = IsName(t[i + 2]);
                if (!baseIsName && t[i + 2].TokenType != TK.REG)
                    return false;
                if (!srcIsName && t[i + 7].TokenType != TK.REG)
                    return false;
                if (!srcIsName && !baseIsName)
                    return false;
                List<Token> bracket = Bracket(t[i].TokenType, t[i + 2], t[i + 4].intValue, srcIsName, o);
                if (srcIsName)
                    AddSlotAccess(o, TK.LDW, Slot(t[i + 7]), Scratch);
                o.Add([Kind(t[i].TokenType, t[i].TokenString), Kind(TK.LSBRACKET, "["), ..bracket, Kind(TK.RSBRACKET, "]"), Comma, srcIsName ? Reg(Scratch) : t[i + 7], Eol]);
                i += 9;
                return true;
            }

            //the bracket of a memory position: a name base is its own slot over R15, or - when the
            //name carries a value - the loaded pointer over r7 (r6 when r7 already carries the value
            //being stored); the author's offset keeps the unit of the access, a word in LDW/STW, a
            //byte in LDB/STB; a loaded pointer is emitted as its own statement before the bracket
            List<Token> Bracket(TK op, Token baseToken, int off, bool intoSecondScratch, List<List<Token>> o)
            {
                var bracket = new List<Token>();
                if (baseToken.TokenType == TK.REG)
                {
                    bracket.Add(baseToken);
                    bracket.Add(Kind(TK.PLUS, "+"));
                    bracket.Add(Num(off));
                    return bracket;
                }
                int slot = Slot(baseToken);
                if (valueDefs.Contains(baseToken.TokenString))
                {
                    int pointer = intoSecondScratch ? SecondScratch : Scratch;
                    AddSlotAccess(o, TK.LDW, slot, pointer);
                    bracket.Add(Reg(pointer));
                    bracket.Add(Kind(TK.PLUS, "+"));
                    bracket.Add(Num(off));
                }
                else
                {
                    bool word = op == TK.LDW || op == TK.STW;
                    bracket.Add(Reg(StackPointer));
                    bracket.Add(Kind(TK.PLUS, "+"));
                    bracket.Add(Num(word ? slot + off : slot * 4 + off));
                }
                return bracket;
            }

            //mov between a name and a register stores/loads the slot directly, name to name goes over r7
            bool Mov(List<Token> t, ref int i, List<List<Token>> o)
            {
                if (i + 4 >= t.Count || t[i + 2].TokenType != TK.COMMA || t[i + 4].TokenType != TK.EOL)
                    return false;
                bool destIsName = IsName(t[i + 1]);
                if (destIsName && IsName(t[i + 3]))
                {
                    AddSlotAccess(o, TK.LDW, Slot(t[i + 3]), Scratch);
                    AddSlotAccess(o, TK.STW, Slot(t[i + 1]), Scratch);
                }
                else if (destIsName && t[i + 3].TokenType == TK.REG)
                    AddSlotAccess(o, TK.STW, Slot(t[i + 1]), t[i + 3].intValue);
                else if (!destIsName && t[i + 1].TokenType == TK.REG && IsName(t[i + 3]))
                    AddSlotAccess(o, TK.LDW, Slot(t[i + 3]), t[i + 1].intValue);
                else
                    return false;
                i += 5;
                return true;
            }

            //'add name, b' loads the slot into r7, runs the operation there and stores it back; a
            //name operand brings r6 as the second scratch
            bool Alu(List<Token> t, ref int i, List<List<Token>> o)
            {
                if (i + 4 >= t.Count || t[i + 2].TokenType != TK.COMMA || t[i + 4].TokenType != TK.EOL)
                    return false;
                bool destIsName = IsName(t[i + 1]);
                bool srcIsName = IsName(t[i + 3]);
                if (destIsName)
                {
                    if (!srcIsName && t[i + 3].TokenType is not (TK.REG or TK.NUMBER))
                        return false;
                    AddSlotAccess(o, TK.LDW, Slot(t[i + 1]), Scratch);
                    Token b = t[i + 3];
                    if (srcIsName)
                    {
                        AddSlotAccess(o, TK.LDW, Slot(b), SecondScratch);
                        b = Reg(SecondScratch);
                    }
                    o.Add([Kind(t[i].TokenType, t[i].TokenString), Reg(Scratch), Comma, b, Eol]);
                    AddSlotAccess(o, TK.STW, Slot(t[i + 1]), Scratch);
                    i += 5;
                    return true;
                }
                if (srcIsName && t[i + 1].TokenType == TK.REG)
                {
                    AddSlotAccess(o, TK.LDW, Slot(t[i + 3]), Scratch);
                    o.Add([Kind(t[i].TokenType, t[i].TokenString), t[i + 1], Comma, Reg(Scratch), Eol]);
                    i += 5;
                    return true;
                }
                return false;
            }

            void Jump(List<List<Token>> o) => o.Add([Kind(TK.J), Literal(retLabel), Eol]);

            void AddRegMove(List<List<Token>> o, int rd, int rs) => o.Add([Kind(TK.MOV), Reg(rd), Comma, Reg(rs), Eol]);

            bool IsName(Token t) => t.TokenType == TK.LITERAL && slots.ContainsKey(t.TokenString);
            int Slot(Token name) => slots[name.TokenString];
        }
    }
}
