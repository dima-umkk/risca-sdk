namespace RiscA.Core.Asm
{
    //What a fixup patches and how the assembler may relax it when the offset does not fit
    //(doc/assembler-architecture.md, 6.2 - 6.4).
    public enum FixupKind
    {
        //Imm8s of BEQZ/BNEZ, steps of instructions (+-128): too far -> invert the branch and add a JR
        Branch,
        //imm of CALL, steps of 4-byte words (+-1024): too far -> LDI r7 from a pool + CALL r7
        Call,
        //imm of JR, steps of instructions (+-1024): too far -> LDI rN from a pool + JMP rN
        Jr,
        //Imm9 of LDI, steps of instructions (+-256): the pool slot (Target != null, relaxes to an
        //inline mini pool when unreachable) or the label itself (a hand-written ldi, not relaxable)
        PoolRef,
        //absolute 32-bit address written into a dw entry: hand-written dw label, a pool slot or an
        //inline mini pool - every absolute address of the image lives in such a dw entry, which is
        //the future relocation table of 'load the program at any address'
        DataAddr,
    }

    //A late-bound reference of one halfword slot of a ParsedInstruction to a label.
    //The parser creates fixups instead of encoding label offsets; pass B of the assembler
    //resolves them against the final addresses and may rewrite the line (relaxation).
    public class Fixup(int slot, string label, FixupKind kind)
    {
        //index of the patched halfword inside ParsedInstruction.Instructions
        public int Slot { get; set; } = slot;
        //label whose address the immediate or the dw entry carries
        public string Label { get; set; } = label;
        public FixupKind Kind { get; set; } = kind;
        //SrcLine of the constant pool slot for the PoolRef of an expanded 'la'/'j'/'call';
        //a hand-written 'ldi r, [label]' has no Target and loads the contents at the label directly
        public SrcLine? Target { get; set; }
        //scratch register remembered by 'j label, rN' for the LDI+JMP fallback (r7 by default)
        public int Reg { get; set; } = 7;
    }
}
