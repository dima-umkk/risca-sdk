using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Core.Asm
{
    public class SrcLine(string line, int pos, string filename, int address = 0, ParsedInstruction? parsedInstruction = null)
    {
        public int Address { get; set; } = address;
        public string Line { get; set; } = line;
        public int Pos { get; set; } = pos;

        public string Filename { get; set; } = filename;
        public ParsedInstruction? ParsedInstruction { get; set; } = parsedInstruction;
    }

    //Two passes (doc/assembler-architecture.md): pass 1 parses every line and lays out the addresses,
    //pass 2 resolves the label fixups and grows the program until all offsets fit (relaxation loop).
    public class Assembler(int address)
    {
        int curAddress = address;

        public List<SrcLine> Src { get {  return src; }  }
        List<SrcLine> src = [];

        Dictionary<string, SrcLine> labelMap = [];
        //constant pools (doc/assembler-architecture.md, 6.3): a dw slot holding the absolute address of
        //a label, keyed by the code region of the reference - the '.func' line the reference sits in
        //(null for the region before the first '.func') and the label
        Dictionary<(SrcLine? owner, string label), SrcLine> pools = [];
        //names of the generated '@overN' labels inserted by the far branch relaxation (doc 6.4)
        int anon;

        //relaxation rounds before the layout is declared hopeless (doc 6.4); real programs converge
        //in two or three rounds, the cap only guards against a pathological source
        const int maxRounds = 10;

        public void Compile(string filename)
        {
            var pre = new Preprocessor();
            pre.Process(filename);

            src.Clear();
            src.AddRange(pre.Lines);
            labelMap.Clear();
            pools.Clear();
            anon = 0;
            curAddress = address;

            //1st pass: parse lines
            for (int i = 0; i < src.Count; i++)
            {
                try
                {
                    if (src[i].ParsedInstruction is not null)
                    {//.equ and .include lines produce no code, so they are not parsed
                        src[i].Address = curAddress;
                        continue;
                    }
                    var pi = Parser.ParseLine(src[i].Line, pre.Constants);
                    if (pi.IsFunc && (curAddress & 0b0000_0011) != 0)
                    {//a function entry must start on a word boundary: put an alignment NOP in
                     //front of it; the nop line is skipped by the loop, the '.func' line itself
                     //comes next - unparsed, so it goes through this block once more, now aligned
                        var nops = new SrcLine(";word align", i, src[i].Filename);
                        nops.ParsedInstruction = new ParsedInstruction();
                        nops.ParsedInstruction.Instructions.Add(new Instruction(0));
                        nops.Address = curAddress;
                        curAddress += 2;
                        src.Insert(i, nops);
                        continue;
                    }
                    src[i].ParsedInstruction = pi;
                    src[i].Address = curAddress;
                    curAddress += pi.Instructions.Count * 2;
                    if (pi.Label is not null)
                    {
                        labelMap[pi.Label] = src[i];
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"{src[i].Filename}:{src[i].Pos + 1}: {ex.Message}");
                }
            }

            //2nd pass: resolve the fixups and relax the offsets that do not fit, until the layout is
            //stable (doc/assembler-architecture.md, 6.4): every expansion inserts instructions, which
            //moves addresses, so the remaining offsets are recomputed in the next round.
            string? lastExpanded = null;
            bool changed = true;
            for (int round = 0; changed; round++)
            {
                if (round == maxRounds)
                    throw new Exception($"{filename}: label relaxation did not converge in {maxRounds} rounds (last expansion involved '{lastExpanded}')");
                RecomputeAddresses();
                changed = ResolveAndRelax(filename, ref lastExpanded);
                if (AlignFuncs())
                    changed = true;//an expansion shifted a function off its word boundary
            }
        }

        void RecomputeAddresses()
        {
            curAddress = address;
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i].ParsedInstruction is not ParsedInstruction pi)
                    continue;
                src[i].Address = curAddress;
                curAddress += pi.Instructions.Count * 2;
            }
        }

        //Resolve every fixup against the current addresses; when an immediate does not fit, expand the
        //line (relaxation) and restart the walk with fresh addresses. Returns true when the program
        //size changed, so another round is needed.
        bool ResolveAndRelax(string filename, ref string? lastExpanded)
        {
            bool changed = false;
        restart:
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i].ParsedInstruction is not ParsedInstruction pi)
                    continue;
                for (int f = 0; f < pi.Fixups.Count; f++)
                {
                    var fx = pi.Fixups[f];
                    int instrAddr = src[i].Address + fx.Slot * 2;
                    switch (fx.Kind)
                    {
                        case FixupKind.DataAddr:
                        {
                            //absolute 32-bit address into a pair of raw halfwords: a hand written
                            //'dw label', a pool slot or an inline mini pool (doc 6.1, 6.3); every
                            //absolute address of the image lives in such a dw entry, which later
                            //doubles as the relocation table of 'load the program anywhere' (doc 7)
                            int addr = ResolveLabel(src[i], fx.Label).Address;
                            if (pi.InlineSlot >= 0)
                            {
                                //inline mini pool: keep the dw pair word aligned with a NOP pad and
                                //refresh the ldi/jr immediates pointing at it (doc 6.1)
                                if (((src[i].Address + fx.Slot * 2) & 0b11) != 0)
                                {
                                    InsertAndShift(pi, fx.Slot, new Instruction(0));
                                    changed = true;
                                    RecomputeAddresses();
                                    goto restart;
                                }
                                int ldiImm = fx.Slot - pi.InlineSlot;
                                pi.Instructions[pi.InlineSlot] = pi.Instructions[pi.InlineSlot].SetImm(ldiImm);
                                //the JR lands right behind the dw pair: +2 halfwords past the dw start
                                pi.Instructions[pi.InlineSlot + 1] = pi.Instructions[pi.InlineSlot + 1].SetImm(ldiImm + 1);
                            }
                            else if (pi.IsPool)
                            {
                                //a pool slot is read by LDI as a word, so keep it word aligned (doc 6.3)
                                if (((src[i].Address + fx.Slot * 2) & 0b11) != 0)
                                {
                                    InsertAndShift(pi, fx.Slot, new Instruction(0));
                                    changed = true;
                                    RecomputeAddresses();
                                    goto restart;
                                }
                            }
                            pi.Instructions[fx.Slot] = new Instruction(unchecked((ushort)(addr & 0xFFFF)));
                            pi.Instructions[fx.Slot + 1] = new Instruction(unchecked((ushort)((addr >> 16) & 0xFFFF)));
                            break;
                        }
                        case FixupKind.Branch:
                        {
                            //branch immediates step in instructions, +-128 (doc/instruction-set.md)
                            var instr = pi.Instructions[fx.Slot];
                            int target = ResolveLabel(src[i], fx.Label).Address;
                            int imm = (target - instrAddr) / 2;
                            if (!Fits(instr, imm))
                            {
                                RelaxBranch(i, pi, fx);
                                lastExpanded = fx.Label;
                                changed = true;
                                RecomputeAddresses();
                                goto restart;
                            }
                            pi.Instructions[fx.Slot] = instr.SetImm(imm);
                            break;
                        }
                        case FixupKind.Jr:
                        {
                            //JR immediates step in instructions, +-1024
                            var instr = pi.Instructions[fx.Slot];
                            int target = ResolveLabel(src[i], fx.Label).Address;
                            int imm = (target - instrAddr) / 2;
                            if (!Fits(instr, imm))
                            {
                                RelaxThroughPool(pi, fx, jump: true);
                                lastExpanded = fx.Label;
                                changed = true;
                                RecomputeAddresses();
                                goto restart;
                            }
                            pi.Instructions[fx.Slot] = instr.SetImm(imm);
                            break;
                        }
                        case FixupKind.Call:
                        {
                            //CALL immediates step in 4 byte words from the word below the instruction
                            //and need a word aligned target (spec line 124)
                            var instr = pi.Instructions[fx.Slot];
                            var refline = ResolveLabel(src[i], fx.Label);
                            int imm = (refline.Address - (instrAddr & ~0b11)) / 4;
                            bool aligned = (refline.Address & 0b11) == 0;
                            if (!Fits(instr, imm))
                            {
                                RelaxThroughPool(pi, fx, jump: false);
                                lastExpanded = fx.Label;
                                changed = true;
                                RecomputeAddresses();
                                goto restart;
                            }
                            if (!aligned)
                                throw new Exception($"{src[i].Filename}: {refline.Pos + 1}: '{fx.Label}' is not word aligned!");
                            pi.Instructions[fx.Slot] = instr.SetImm(imm);
                            break;
                        }
                        case FixupKind.PoolRef:
                        {
                            var instr = pi.Instructions[fx.Slot];
                            if (fx.Target is null && pi.PoolRequests.Contains(fx.Label))
                            {
                                //'la'/'j'/'call' expansion: bind to the constant pool slot of the
                                //function region, creating it at the end of the region if needed (doc 6.3)
                                SrcLine slot = GetPool(i, fx.Label, out bool created);
                                fx.Target = slot;
                                if (created)
                                {
                                    changed = true;
                                    RecomputeAddresses();
                                    goto restart;
                                }
                            }
                            int imm;
                            if (fx.Target is SrcLine slotLine)
                                imm = (slotLine.Address + slotLine.ParsedInstruction!.Fixups[0].Slot * 2 - instrAddr) / 2;
                            else
                                //a hand written 'ldi r, [label]' reads the contents at the label,
                                //there is no data to move around, so it cannot be relaxed (doc 6.1)
                                imm = (ResolveLabel(src[i], fx.Label).Address - instrAddr) / 2;
                            if (!Fits(instr, imm))
                            {
                                if (fx.Target is null)
                                    throw new Exception($"{src[i].Filename}: {src[i].Pos + 1}: Reference address to far ({imm})! Offset must be in range -256 .. 255 for LDI; load the address with 'la' and use a register load instead");
                                //the pool slot is out of LDI reach: an inline mini pool on the spot,
                                //the dead pool slot is never reclaimed and there is no switch back,
                                //which keeps the rounds monotone (doc 6.1, 6.4)
                                MakeInlinePool(pi, fx);
                                lastExpanded = fx.Label;
                                changed = true;
                                RecomputeAddresses();
                                goto restart;
                            }
                            pi.Instructions[fx.Slot] = instr.SetImm(imm);
                            break;
                        }
                    }
                }
            }
            return changed;
        }

        SrcLine ResolveLabel(SrcLine line, string label)
        {
            if (!labelMap.TryGetValue(label, out SrcLine? refline))
                throw new Exception($"{line.Filename}: {line.Pos + 1}: Unknown label '{label}'");
            return refline;
        }

        static bool Fits(Instruction instr, int imm)
        {
            try
            {
                instr.CheckImmLimits(imm);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        //insert an instruction at index p and shift the Slot of every fixup that now points past it
        static void InsertAndShift(ParsedInstruction pi, int p, Instruction instr)
        {
            pi.Instructions.Insert(p, instr);
            foreach (var fx in pi.Fixups)
                if (fx.Slot >= p)
                    fx.Slot++;
        }

        //far branch: invert it and put an unconditional JR behind it (doc 6.4):
        //when the inverted condition holds, the branch skips over the JR, otherwise the JR runs
        void RelaxBranch(int i, ParsedInstruction pi, Fixup fx)
        {
            int slot = fx.Slot;
            string target = fx.Label;
            var branch = pi.Instructions[slot];
            pi.Instructions[slot] = new Instruction(0)
                .withOpCode(OpCode.BRANCH)
                .withFunc1(branch.Func1 ^ 1)//BEQZ <-> BNEZ
                .withRd(branch.Rd);
            InsertAndShift(pi, slot + 1, new Instruction(0)
                .withOpCode(OpCode.CALL_JMP_RET)
                .withFunc2((int)CallJmpRetFunc.JR));
            //a zero instruction line carries the hidden label the inverted branch skips over
            string over = $"@over{anon++}";
            var overLine = new SrcLine($"\t{over}: ;fallthrough of the relaxed branch to '{target}'", src[i].Pos, src[i].Filename)
            {
                ParsedInstruction = new ParsedInstruction { Label = over },
            };
            src.Insert(i + 1, overLine);
            labelMap[over] = overLine;
            fx.Label = over;
            //the inserted JR takes the original target and relaxes itself if it is too far as well
            pi.Fixups.Add(new Fixup(slot + 1, target, FixupKind.Jr) { Reg = fx.Reg });
        }

        //far JR/CALL: load the absolute address from the constant pool and go through a register (doc 6.4);
        //JR becomes LDI+JMP rN (rN from 'j label, rN', r7 by default), CALL always uses scratch r7 (ABI)
        void RelaxThroughPool(ParsedInstruction pi, Fixup fx, bool jump)
        {
            int slot = fx.Slot;
            int reg = jump && fx.Reg is >= 0 and <= 13 ? fx.Reg : 7;
            pi.Instructions[slot] = new Instruction(0)
                .withOpCode(OpCode.LDI)
                .withRd(reg);
            InsertAndShift(pi, slot + 1, new Instruction(0)
                .withOpCode(OpCode.CALL_JMP_RET)
                .withFunc2(jump ? (int)CallJmpRetFunc.RET : (int)CallJmpRetFunc.CALL_REG)//RET with Rd != 14 is JMP Rd
                .withRd(reg));
            fx.Kind = FixupKind.PoolRef;
            fx.Target = null;
            pi.PoolRequests.Add(fx.Label);
        }

        //unreachable pool slot: 'la' expands into an inline mini pool right on the spot:
        //ldi +2 / jr +3 over the data / dw label, optionally a NOP pad before the dw (doc 6.1);
        //the reference fixup turns into the dw pair itself, so the expansion is never undone
        void MakeInlinePool(ParsedInstruction pi, Fixup fx)
        {
            if (pi.InlineSlot >= 0)
                throw new Exception($"'{fx.Label}' is out of LDI reach and this line already carries an inline constant pool");
            int slot = fx.Slot;
            int rd = pi.Instructions[slot].Rd;
            pi.Instructions[slot] = new Instruction(0)
                .withOpCode(OpCode.LDI)
                .withRd(rd);
            InsertAndShift(pi, slot + 1, new Instruction(0)
                .withOpCode(OpCode.CALL_JMP_RET)
                .withFunc2((int)CallJmpRetFunc.JR));
            InsertAndShift(pi, slot + 2, new Instruction(0));
            InsertAndShift(pi, slot + 3, new Instruction(0));
            fx.Kind = FixupKind.DataAddr;
            fx.Slot = slot + 2;
            fx.Target = null;
            pi.InlineSlot = slot;
        }

        //the dw slot of a constant pool for (region of line i, label), created at the end of the
        //region - right before the next '.func' line, or at the end of the file (doc 6.3)
        SrcLine GetPool(int i, string label, out bool created)
        {
            SrcLine? owner = null;
            for (int k = i - 1; k >= 0; k--)
                if (src[k].ParsedInstruction?.IsFunc == true)
                {
                    owner = src[k];
                    break;
                }
            created = false;
            if (!pools.TryGetValue((owner, label), out SrcLine? slot))
            {
                int at = src.Count;
                for (int k = i + 1; k < src.Count; k++)
                    if (src[k].ParsedInstruction?.IsFunc == true)
                    {
                        at = k;
                        break;
                    }
                slot = new SrcLine($"\tdw {label} ;pool", src[i].Pos, src[i].Filename);
                slot.ParsedInstruction = new ParsedInstruction { IsDW = true, IsPool = true };
                slot.ParsedInstruction.Instructions.Add(new Instruction(0));
                slot.ParsedInstruction.Instructions.Add(new Instruction(0));
                slot.ParsedInstruction.Fixups.Add(new Fixup(0, label, FixupKind.DataAddr));
                src.Insert(at, slot);
                pools[(owner, label)] = slot;
                created = true;
            }
            return slot;
        }

        //a relaxation may grow the code by an odd number of instructions and shift the next '.func'
        //off its word boundary, so re-pad the function entry the same way pass 1 does
        bool AlignFuncs()
        {
            bool inserted = false;
            for (int i = 0; i < src.Count; i++)
            {
                if (src[i].ParsedInstruction?.IsFunc == true && (src[i].Address & 0b11) != 0)
                {
                    var nop = new SrcLine(";word align", src[i].Pos, src[i].Filename);
                    nop.ParsedInstruction = new ParsedInstruction();
                    nop.ParsedInstruction.Instructions.Add(new Instruction(0));
                    src.Insert(i, nop);
                    inserted = true;
                }
            }
            return inserted;
        }
    }
}
