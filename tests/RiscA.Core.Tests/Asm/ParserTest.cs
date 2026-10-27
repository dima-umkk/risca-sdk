using RiscA.Core.Asm;
using RiscA.Core.ISA;
using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Core.Tests.Asm
{
    public class ParserTest
    {
        [Theory]
        [InlineData("mov r1, r0", new int[] { 0, 1, 0 })]
        [InlineData("add r2, r15", new int[] { 1, 2, 15 })]
        [InlineData("sub r3, r14", new int[] { 2, 3, 14 })]
        [InlineData("and r4, r13", new int[] { 3, 4, 13 })]
        [InlineData("or r5, r12", new int[] { 4, 5, 12 })]
        [InlineData("xor r6, r11", new int[] { 5, 6, 11 })]
        [InlineData("not r7, r10", new int[] { 6, 7, 10 })]
        [InlineData("mul r8, r9", new int[] { 7, 8, 9 })]
        [InlineData("slt r9, r8", new int[] { 8, 9, 8 })]
        [InlineData("sltu r10, r7", new int[] { 9, 10, 7 })]
        public void ParserAluRegRegTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.ALU_REG_REG);
            pi.Instructions[0].Func4.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Rs.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("shl r1, 15",  new int[] { 0, 1, 15 })]
        [InlineData("shr r2, 2",   new int[] { 1, 2, 2 })]
        [InlineData("add r3, 127", new int[] { 2, 3, 127 })]
        [InlineData("sub r4, 3",   new int[] { 3, 4, 3 })]
        public void ParserAluRegImmTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.ALU_REG_IMM);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("shl r1, 0x0F", new int[] { 0, 1, 15 })]
        [InlineData("shr r2, 0b10", new int[] { 1, 2, 2 })]
        [InlineData("add r3, 0x7F", new int[] { 2, 3, 127 })]
        [InlineData("sub r4, 0b11", new int[] { 3, 4, 3 })]
        public void ParserAluRegImmHexBinTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.ALU_REG_IMM);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("shl r1, 16", "0 .. 15")]
        [InlineData("shl r1, 33", "0 .. 15")]
        [InlineData("shr r2, 222", "Shift must be in range 0 .. 15")]
        [InlineData("add r3, 128", "0 .. 127")]
        [InlineData("sub r4, 300", "0 .. 127")]
        public void ParserAluRegImmExceptionTest(string line, string exstr)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("movi r1, 31", new int[] { 0, 1, 31 })]
        [InlineData("movl r12, 2", new int[] { 1, 12, 2 })]
        public void ParserRegImmTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.REG_IMM);
            pi.Instructions[0].Func1.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm8.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("movi r1, 256", "0 .. 255")]
        [InlineData("movl r12, 300", "0 .. 255")]
        public void ParserRegImmExceptionTest(string line, string exstr)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("ldb r1,  [r2  + 3]", new int[] { 0, 1,  2,  3 })]
        [InlineData("ldw r12, [r14 + 7]", new int[] { 1, 12, 14, 7 })]
        public void ParserLDTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.ST_LD);
            pi.Instructions[0].Func21.Should().Be(0);
            pi.Instructions[0].Func22.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Rs.Should().Be(result[2]);
            pi.Instructions[0].Imm3.Should().Be(result[3]);
        }

        [Theory]
        [InlineData("ldb r1,  [r2  + 8]", "0 .. 7")]
        [InlineData("ldw r12, [r14 + 300]", "0 .. 7")]
        public void ParserLDExceptionTest(string line, string exstr)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("stb [r2  + 3], r1",  new int[] { 0, 1, 2, 3 })]
        [InlineData("stw [r14 + 7], r12", new int[] { 1, 12, 14, 7 })]
        public void ParserSTTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.ST_LD);
            pi.Instructions[0].Func21.Should().Be(1);
            pi.Instructions[0].Func22.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Rs.Should().Be(result[2]);
            pi.Instructions[0].Imm3.Should().Be(result[3]);
        }

        [Theory]
        [InlineData("stb [r2  + 8], r1", "0 .. 7")]
        [InlineData("stw [r14 + 999], r12", "0 .. 7")]
        public void ParserSTExceptionTest(string line, string exstr)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("beqz r1, 31",   new int[] { 0, 1, 31 })]
        [InlineData("bnez r2, 2",    new int[] { 1, 2, 2 })]
        [InlineData("beqz r3, 127",  new int[] { 0, 3, 127 })]
        [InlineData("bnez r4, -128", new int[] { 1, 4, -128 })]
        public void ParserBranchTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.BRANCH);
            pi.Instructions[0].Func1.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm8s.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("beqz r1, label1",  new int[] { 0, 1}, "label1")]
        [InlineData("bnez r2, loop",    new int[] { 1, 2}, "loop")]
        public void ParserBranchLabelsTest(string line, int[] result, string label)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.BRANCH);
            pi.Instructions[0].Func1.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm8s.Should().Be(0);
            pi.Fixups.Should().ContainSingle(f => f.Label == label && f.Kind == FixupKind.Branch && f.Slot == 0);
        }

        [Theory]
        [InlineData("beqz r1, -65*2", "-128 .. 127")]
        [InlineData("bnez r2, 256/2", "-128 .. 127")]
        public void ParserBranchExceptionsTest(string line, string exstr)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("add r3, (-1)*-127", new int[] { 2, 3, 127 })]
        [InlineData("add r3, (-5+511*(-1))/(-5)", new int[] { 2, 3, 103 })]
        [InlineData("add r3,  -5-100*(-1)", new int[] { 2, 3, 95 })]
        [InlineData("add r3, 50+30",      new int[] { 2, 3, 80 })]
        [InlineData("add r3, 100-30",     new int[] { 2, 3, 70 })]
        [InlineData("add r3, 7*8",        new int[] { 2, 3, 56 })]
        [InlineData("add r3, 100/4",      new int[] { 2, 3, 25 })]
        [InlineData("add r3, (50)",       new int[] { 2, 3, 50 })]
        [InlineData("add r3, ((50))",     new int[] { 2, 3, 50 })]
        [InlineData("add r3, -50+80",     new int[] { 2, 3, 30 })]
        [InlineData("add r3, -10*-5",     new int[] { 2, 3, 50 })]
        [InlineData("add r3, 50+-25",     new int[] { 2, 3, 25 })]
        [InlineData("add r3, 5--3",       new int[] { 2, 3, 8 })]
        [InlineData("add r3, 10+5*2",     new int[] { 2, 3, 20 })]
        [InlineData("add r3, 10*5+2",     new int[] { 2, 3, 52 })]
        [InlineData("add r3, 20-5*2",     new int[] { 2, 3, 10 })]
        [InlineData("add r3, (10+5)*2",   new int[] { 2, 3, 30 })]
        [InlineData("add r3, 2*3*4",      new int[] { 2, 3, 24 })]
        [InlineData("add r3, 10/3",       new int[] { 2, 3, 3 })]
        public void ParseExpressionTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.ALU_REG_IMM);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(result[2]);
        }


        [Theory]
        [InlineData("add r3, (-1)*(32%3)", "%")]
        [InlineData("add r3, (-1)*(256/2)", "-128")]
        [InlineData("add r3, -(2)", "0 .. 127")]
        [InlineData("add r3, 70+60", "0 .. 127")]
        [InlineData("shl r1, 20+20", "0 .. 15")]
        [InlineData("shr r1, 100-50", "0 .. 15")]
        [InlineData("ldi r5, [-200-200-200]", "-256 .. 255")]
        public void ParseExpressionExceptionTest(string line, string exstr)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("bnez r2, 10+20",    new int[] { 1, 2, 30 })]
        [InlineData("beqz r3, -7+8*2",   new int[] { 0, 3, 9 })]
        [InlineData("bnez r2, 100-80",   new int[] { 1, 2, 20 })]
        [InlineData("beqz r1, (10+5)*2", new int[] { 0, 1, 30 })]
        [InlineData("bnez r2, -100-20",  new int[] { 1, 2, -120 })]
        public void ParseExpressionWithBranchTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.BRANCH);
            pi.Instructions[0].Func1.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm8s.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("movi r5, 50+50",  new int[] { 0, 5, 100 })]
        [InlineData("movl r12, 200-50", new int[] { 1, 12, 150 })]
        [InlineData("movi r7, 5*30",    new int[] { 0, 7, 150 })]
        public void ParseExpressionWithRegImmTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.REG_IMM);
            pi.Instructions[0].Func1.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm8.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("ldi r15, [label1]", new int[] { 15 }, "label1")]
        public void ParserLDILabelsTest(string line, int[] result, string label)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.LDI);
            pi.Instructions[0].Rd.Should().Be(result[0]);
            pi.Instructions[0].Imm7.Should().Be(0);
            //a hand written 'ldi r, [label]' loads the contents at the label: a PoolRef fixup
            //with no pool slot target, so it is resolved directly and never relaxed
            pi.Fixups.Should().ContainSingle(f => f.Label == label && f.Kind == FixupKind.PoolRef && f.Target == null && f.Slot == 0);
        }

        [Theory]
        [InlineData("ldi r5, -5+511*(-1)", "-516")]
        public void ParseLDIExceptionTest(string line, string exstr)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("call 0",    new int[] { 0, 0, 0 })]
        [InlineData("call 31",   new int[] { 0, 15, 1 })]
        [InlineData("call 1023", new int[] { 0, 15, 63 })]
        [InlineData("jr 16",     new int[] { 3, 0, 1 })]
        [InlineData("jr 255",    new int[] { 3, 15, 15 })]
        [InlineData("call -1024",new int[] { 0, 0, 64 })]
        [InlineData("jr -1",     new int[] { 3, 15, 127 })]
        public void ParserCallJrImmTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.CALL_JMP_RET);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("call label1", new int[] { 0 }, "label1")]
        [InlineData("jr loop",     new int[] { 3 }, "loop")]
        public void ParserCallJrLabelsTest(string line, int[] result, string label)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.CALL_JMP_RET);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(0);
            pi.Instructions[0].Imm7.Should().Be(0);
            pi.Fixups.Should().ContainSingle(f => f.Label == label && f.Slot == 0
                && f.Kind == (result[0] == 0 ? FixupKind.Call : FixupKind.Jr));
        }

        [Theory]
        [InlineData("call 2000",  "-1024 .. 1023")]
        [InlineData("jr -2000",   "-1024 .. 1023")]
        [InlineData("call -1025", "-1024 .. 1023")]
        [InlineData("jr 1024",    "-1024 .. 1023")]
        public void ParserCallJrExceptionTest(string line, string exstr)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("call 0+1",  new int[] { 0, 1, 0 })]
        [InlineData("call 2*2",  new int[] { 0, 4, 0 })]
        [InlineData("jr 4+3",    new int[] { 3, 7, 0 })]
        [InlineData("call 8*2",  new int[] { 0, 0, 1 })]
        public void ParserCallJrExpressionTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.CALL_JMP_RET);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("call r5", new int[] { 1, 5 })]
        [InlineData("jmp r7",  new int[] { 2, 7 })]
        public void ParserCallJmpRegTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.CALL_JMP_RET);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
        }

        [Theory]
        [InlineData("int r3", new int[] { 0, 3 })]
        public void ParserIntRegTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.INT_RETI);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
        }

        [Theory]
        [InlineData("ret",  new int[] { 2, 14 })]
        [InlineData("reti", new int[] { 1, 0 })]
        public void ParserRetRetiTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(result[0] == 1 ? OpCode.INT_RETI : OpCode.CALL_JMP_RET);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
        }

        [Theory]
        [InlineData("mov r5, epc", new int[] { 2, 5 })]
        [InlineData("mov epc, r7", new int[] { 3, 7 })]
        public void ParserEPCTest(string line, int[] result)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.INT_RETI);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
        }

        [Theory]
        [InlineData("db 'A'",  new ushort[] { 0x0041 })]
        [InlineData("db 'AB'", new ushort[] { 0x4241 })]
        [InlineData("db 'ABC'", new ushort[] { 0x4241, 0x0043 })]
        [InlineData("db 'hello'", new ushort[] { 0x6568, 0x6C6C, 0x006F })]
        [InlineData("db 65",   new ushort[] { 0x0041 })]
        [InlineData("db 0",    new ushort[] { 0x0000 })]
        [InlineData("db 255",  new ushort[] { 0x00FF })]
        [InlineData("db -128", new ushort[] { 0x0080 })]
        public void ParseDBStringNumberTest(string line, ushort[] expected)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(expected.Length);
            for (int i = 0; i < expected.Length; i++)
                pi.Instructions[i].Raw.Should().Be(expected[i]);
        }

        [Theory]
        [InlineData("db 'hello', 32, 'world'", new ushort[] { 0x6568, 0x6C6C, 0x206F, 0x6F77, 0x6C72, 0x0064 })]
        [InlineData("db 'AB', 0x43, 'D'", new ushort[] { 0x4241, 0x4443 })]
        public void ParseDBMixedTest(string line, ushort[] expected)
        {
            var pi = Parser.ParseLine(line);
            pi.Instructions.Should().HaveCount(expected.Length);
            for (int i = 0; i < expected.Length; i++)
                pi.Instructions[i].Raw.Should().Be(expected[i]);
        }

        [Fact]
        public void ParseDBEmptyStringTest()
        {
            var pi = Parser.ParseLine("db ''");
            pi.Instructions.Should().BeEmpty();
        }

        [Theory]
        [InlineData("label: db 'A'", "label", 0x0041)]
        [InlineData("data: db 42", "data", 0x002A)]
        public void ParseDBWithLabelTest(string line, string label, ushort raw)
        {
            var pi = Parser.ParseLine(line);
            pi.Label.Should().Be(label);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].Raw.Should().Be(raw);
        }

        [Theory]
        [InlineData("db 256")]
        [InlineData("db -129")]
        public void ParseDBRangeErrorTest(string line)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage("*out of range*");
        }

        //the tokenizer reads both quote styles ('.include "file"' needs the double ones), but db
        //data stays single quoted so the source of a program keeps one spelling for strings
        [Theory]
        [InlineData("db \"hello\"")]
        [InlineData("db \"A\", 'B'")]
        public void ParseDBRejectsDoubleQuotedStringTest(string line)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage("*must be single quoted*");
        }

        [Theory]
        [InlineData("dw 0x12345678", new ushort[] { 0x5678, 0x1234 })]
        [InlineData("dw -1", new ushort[] { 0xFFFF, 0xFFFF })]
        [InlineData("dw 0", new ushort[] { 0x0000, 0x0000 })]
        [InlineData("dw 0x7FFFFFFF", new ushort[] { 0xFFFF, 0x7FFF })]
        public void ParseDWNumberTest(string line, ushort[] expected)
        {
            var pi = Parser.ParseLine(line);
            pi.IsDW.Should().BeTrue();
            pi.Instructions.Should().HaveCount(expected.Length);
            for (int i = 0; i < expected.Length; i++)
                pi.Instructions[i].Raw.Should().Be(expected[i]);
        }

        [Theory]
        [InlineData("dw label1", "label1")]
        [InlineData("dw mylabel", "mylabel")]
        public void ParseDWLabelTest(string line, string label)
        {
            var pi = Parser.ParseLine(line);
            pi.IsDW.Should().BeTrue();
            pi.Fixups.Should().ContainSingle(f => f.Label == label && f.Kind == FixupKind.DataAddr && f.Slot == 0);
            pi.Instructions.Should().HaveCount(2);
            pi.Instructions[0].Raw.Should().Be(0);
            pi.Instructions[1].Raw.Should().Be(0);
        }

        [Theory]
        [InlineData("data: dw 42", "data", new ushort[] { 0x002A, 0x0000 })]
        public void ParseDWWithLabelTest(string line, string label, ushort[] expected)
        {
            var pi = Parser.ParseLine(line);
            pi.Label.Should().Be(label);
            pi.IsDW.Should().BeTrue();
            pi.Instructions.Should().HaveCount(expected.Length);
            for (int i = 0; i < expected.Length; i++)
                pi.Instructions[i].Raw.Should().Be(expected[i]);
        }

        [Theory]
        [InlineData("dw 'hello'")]
        public void ParseDWStringErrorTest(string line)
        {
            Action act = () => Parser.ParseLine(line);
            act.Should().Throw<Exception>().WithMessage("*syntax error*");
        }

        [Theory]
        //li Rd, value and la Rd, <number> build the MOVI/MOVL chain from the highest non zero
        //byte of the constant (doc/assembler-architecture.md, 6.1): func 0 is MOVI, func 1 is MOVL
        [InlineData("li r0, 0",          new int[] { 0 },          new int[] { 0 })]
        [InlineData("li r3, 0xFF",       new int[] { 0 },          new int[] { 255 })]
        [InlineData("li r1, 0x100",      new int[] { 0, 1 },       new int[] { 1, 0 })]
        [InlineData("li r2, 0x12345678", new int[] { 0, 1, 1, 1 }, new int[] { 0x12, 0x34, 0x56, 0x78 })]
        [InlineData("li r4, -1",         new int[] { 0, 1, 1, 1 }, new int[] { 255, 255, 255, 255 })]
        [InlineData("la r5, 0x100",      new int[] { 0, 1 },       new int[] { 1, 0 })]
        public void ParserLiChainTest(string line, int[] funcs, int[] imm8s)
        {
            var pi = Parser.ParseLine(line);

            pi.Instructions.Should().HaveCount(imm8s.Length);
            for (int i = 0; i < imm8s.Length; i++)
            {
                pi.Instructions[i].OpCode.Should().Be(OpCode.REG_IMM);
                pi.Instructions[i].Func1.Should().Be(funcs[i]);
                pi.Instructions[i].Imm8.Should().Be(imm8s[i]);
            }
            //the chain writes to one and the same register, the fixup list stays empty
            pi.Instructions.Should().OnlyContain(x => x.Rd == int.Parse(line.Substring(4, 1)));
            pi.Fixups.Should().BeEmpty();
            pi.PoolRequests.Should().BeEmpty();
        }

        [Fact]
        public void ParserLaLabelTest()
        {
            //la Rd, label leaves an LDI placeholder bound to the constant pool slot by pass B (doc 6.3)
            var pi = Parser.ParseLine("la r2, msg");

            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.LDI);
            pi.Instructions[0].Rd.Should().Be(2);
            pi.Instructions[0].Imm7.Should().Be(0);
            pi.Fixups.Should().ContainSingle(f => f.Label == "msg" && f.Kind == FixupKind.PoolRef && f.Target == null && f.Slot == 0);
            pi.PoolRequests.Should().ContainSingle().Which.Should().Be("msg");
        }

        [Theory]
        [InlineData("j target", 7)]    //r7 is the scratch register of the LDI+JMP fallback by default
        [InlineData("j target, r3", 3)]
        public void ParserJLabelTest(string line, int reg)
        {
            var pi = Parser.ParseLine(line);

            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.CALL_JMP_RET);
            pi.Instructions[0].Func2.Should().Be((int)CallJmpRetFunc.JR);
            pi.Instructions[0].Imm7.Should().Be(0);
            pi.Fixups.Should().ContainSingle(f => f.Label == "target" && f.Kind == FixupKind.Jr && f.Slot == 0 && f.Reg == reg);
        }

        [Fact]
        public void ParserJNumberTest()
        {
            //j <number> is a plain immediate jump, encoded with the CALL/JR split like 'call <number>'
            var pi = Parser.ParseLine("j 20");
            pi.Instructions[0].ImmCallJr.Should().Be(20);
            pi.Fixups.Should().BeEmpty();

            Action act = () => Parser.ParseLine("j 20000");
            act.Should().Throw<Exception>().WithMessage("*-1024 .. 1023*");
        }

        [Theory]
        //push rN is the PUSH macro of doc/RiscA.MD ("Stack") expanded verbatim
        [InlineData("push r3", 3)]
        [InlineData("push r0", 0)]
        [InlineData("push r14", 14)]
        public void ParserPushTest(string line, int rd)
        {
            var pi = Parser.ParseLine(line);

            pi.Instructions.Should().HaveCount(2);
            //SUB R15, 4 first, the slot exists before the store
            pi.Instructions[0].OpCode.Should().Be(OpCode.ALU_REG_IMM);
            pi.Instructions[0].Func2.Should().Be((int)ALUImmFunc.SUB);
            pi.Instructions[0].Rd.Should().Be(15);
            pi.Instructions[0].Imm7.Should().Be(4);
            pi.Instructions[1].OpCode.Should().Be(OpCode.ST_LD);
            pi.Instructions[1].Func21.Should().Be((int)LdStFunc.ST);
            pi.Instructions[1].Func22.Should().Be((int)LdStBWFunc.W);
            pi.Instructions[1].Rs.Should().Be(15);
            pi.Instructions[1].Imm3.Should().Be(0);
            pi.Instructions[1].Rd.Should().Be(rd);
            pi.Fixups.Should().BeEmpty();
            pi.PoolRequests.Should().BeEmpty();
        }

        [Fact]
        public void ParserPopTest()
        {
            var pi = Parser.ParseLine("pop r4");

            pi.Instructions.Should().HaveCount(2);
            //LDW rN, [R15+0] first and only then ADD R15, 4: the exact reverse of push,
            //which is what makes a push/pop pair return R15 to where it started
            pi.Instructions[0].OpCode.Should().Be(OpCode.ST_LD);
            pi.Instructions[0].Func21.Should().Be((int)LdStFunc.LD);
            pi.Instructions[0].Func22.Should().Be((int)LdStBWFunc.W);
            pi.Instructions[0].Rd.Should().Be(4);
            pi.Instructions[0].Rs.Should().Be(15);
            pi.Instructions[0].Imm3.Should().Be(0);
            pi.Instructions[1].OpCode.Should().Be(OpCode.ALU_REG_IMM);
            pi.Instructions[1].Func2.Should().Be((int)ALUImmFunc.ADD);
            pi.Instructions[1].Rd.Should().Be(15);
            pi.Instructions[1].Imm7.Should().Be(4);
            pi.Fixups.Should().BeEmpty();
        }

        [Fact]
        public void PushOrPopOfTheStackPointerIsAnError()
        {
            //r15 is the pointer itself: moving it and then storing through it is never intended
            Action act = () => Parser.ParseLine("push r15");
            act.Should().Throw<Exception>().WithMessage("*stack pointer cannot be pushed or popped*");

            act = () => Parser.ParseLine("pop r15");
            act.Should().Throw<Exception>().WithMessage("*stack pointer cannot be pushed or popped*");
        }

        [Fact]
        public void PushAndPopAreReservedWords()
        {
            Tokenizer.IsReserved("push").Should().BeTrue();
            Tokenizer.IsReserved("POP").Should().BeTrue();
            //a mnemonic is not a LITERAL, so it cannot become a label
            Action act = () => Parser.ParseLine("push: nop");
            act.Should().Throw<Exception>().WithMessage("*Syntax error*");
        }
    }
}
