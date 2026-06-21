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
        public void ParserAluRegRegTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.ALU_REG_REG);
            pi.Instructions[0].Func3.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Rs.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("shl r1, 31",  new int[] { 0, 1, 31 })]
        [InlineData("shr r2, 2",   new int[] { 1, 2, 2 })]
        [InlineData("add r3, 127", new int[] { 2, 3, 127 })]
        [InlineData("sub r4, 3",   new int[] { 3, 4, 3 })]
        public void ParserAluRegImmTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.ALU_REG_IMM);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("shl r1, 0x1F", new int[] { 0, 1, 31 })]
        [InlineData("shr r2, 0b10", new int[] { 1, 2, 2 })]
        [InlineData("add r3, 0x7F", new int[] { 2, 3, 127 })]
        [InlineData("sub r4, 0b11", new int[] { 3, 4, 3 })]
        public void ParserAluRegImmHexBinTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.ALU_REG_IMM);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("shl r1, 33", "0 .. 32")]
        [InlineData("shr r2, 222", "0 .. 32")]
        [InlineData("add r3, 128", "0 .. 127")]
        [InlineData("sub r4, 300", "0 .. 127")]
        public void ParserAluRegImmExceptionTest(string line, string exstr)
        {
            Parser p = new Parser();
            Action act = () => p.ParseLine("test.rasm", line, 1);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("movi r1, 31", new int[] { 0, 1, 31 })]
        [InlineData("movl r12, 2", new int[] { 1, 12, 2 })]
        public void ParserRegImmTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.REG_IMM);
            pi.Instructions[0].Func1.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm8.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("movi r1, 256", "<= 255")]
        [InlineData("movl r12, 300", "<= 255")]
        public void ParserRegImmExceptionTest(string line, string exstr)
        {
            Parser p = new Parser();
            Action act = () => p.ParseLine("test.rasm", line, 1);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("ldb r1,  [r2  + 3]", new int[] { 0, 1,  2,  3 })]
        [InlineData("ldw r12, [r14 + 7]", new int[] { 1, 12, 14, 7 })]
        public void ParserLDTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
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
            Parser p = new Parser();
            Action act = () => p.ParseLine("test.rasm", line, 1);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("stb [r2  + 3], r1",  new int[] { 0, 1, 2, 3 })]
        [InlineData("stw [r14 + 7], r12", new int[] { 1, 12, 14, 7 })]
        public void ParserSTTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
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
            Parser p = new Parser();
            Action act = () => p.ParseLine("test.rasm", line, 1);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("beqz r1, 31",  new int[] { 0, 1, 31 })]
        [InlineData("bnez r2, 2",   new int[] { 1, 2, 2 })]
        [InlineData("bgtz r3, 63", new int[] { 2, 3, 63 })]
        [InlineData("bltz r4, 3",   new int[] { 3, 4, 3 })]
        public void ParserBranchTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.BRANCH);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("beqz r1, label1",  new int[] { 0, 1}, "label1")]
        [InlineData("bnez r2, loop",    new int[] { 1, 2}, "loop")]
        [InlineData("bgtz r3, main33n", new int[] { 2, 3}, "main33n")]
        [InlineData("bltz r4, asd234",  new int[] { 3, 4}, "asd234")]
        public void ParserBranchLabelsTest(string line, int[] result, string label)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.BRANCH);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(0);
            pi.RefLabel.Should().Be(label);
        }

        [Theory]
        [InlineData("beqz r1, -35*2", "-64 .. 63")]
        [InlineData("bnez r2, 256/2/2", "-64 .. 63")]
        public void ParserBranchExceptionsTest(string line, string exstr)
        {
            Parser p = new Parser();
            Action act = () => p.ParseLine("test.rasm", line, 1);
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
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
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
        [InlineData("shl r1, 20+20", "0 .. 32")]
        [InlineData("shr r1, 100-50", "0 .. 32")]
        [InlineData("ldi r5, -200-200-200", "-512 .. 511")]
        public void ParseExpressionExceptionTest(string line, string exstr)
        {
            Parser p = new Parser();
            Action act = () => p.ParseLine("test.rasm", line, 1);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("bnez r2, 10+20",    new int[] { 1, 2, 30 })]
        [InlineData("bgtz r3, -7+8*2",   new int[] { 2, 3, 9 })]
        [InlineData("bnez r2, 100-80",   new int[] { 1, 2, 20 })]
        [InlineData("beqz r1, (10+5)*2", new int[] { 0, 1, 30 })]
        public void ParseExpressionWithBranchTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.BRANCH);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm7.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("movi r5, 50+50",  new int[] { 0, 5, 100 })]
        [InlineData("movl r12, 200-50", new int[] { 1, 12, 150 })]
        [InlineData("movi r7, 5*30",    new int[] { 0, 7, 150 })]
        public void ParseExpressionWithRegImmTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.REG_IMM);
            pi.Instructions[0].Func1.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
            pi.Instructions[0].Imm8.Should().Be(result[2]);
        }

        [Theory]
        [InlineData("ldi r15, label1", new int[] { 15 }, "label1")]
        public void ParserLDILabelsTest(string line, int[] result, string label)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.LDI);
            pi.Instructions[0].Rd.Should().Be(result[0]);
            pi.Instructions[0].Imm7.Should().Be(0);
            pi.RefLabel.Should().Be(label);
        }

        [Theory]
        [InlineData("ldi r5, -5+511*(-1)", "-516")]
        public void ParseLDIExceptionTest(string line, string exstr)
        {
            Parser p = new Parser();
            Action act = () => p.ParseLine("test.rasm", line, 1);
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
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
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
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.CALL_JMP_RET);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(0);
            pi.Instructions[0].Imm7.Should().Be(0);
            pi.RefLabel.Should().Be(label);
        }

        [Theory]
        [InlineData("call 2000",  "-1024 .. 1023")]
        [InlineData("jr -2000",   "-1024 .. 1023")]
        [InlineData("call -1025", "-1024 .. 1023")]
        [InlineData("jr 1024",    "-1024 .. 1023")]
        public void ParserCallJrExceptionTest(string line, string exstr)
        {
            Parser p = new Parser();
            Action act = () => p.ParseLine("test.rasm", line, 1);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        }

        [Theory]
        [InlineData("call 0+1",  new int[] { 0, 1, 0 })]
        [InlineData("call 2*2",  new int[] { 0, 4, 0 })]
        [InlineData("jr 4+3",    new int[] { 3, 7, 0 })]
        [InlineData("call 8*2",  new int[] { 0, 0, 1 })]
        public void ParserCallJrExpressionTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
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
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.CALL_JMP_RET);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
        }

        [Theory]
        [InlineData("int r3", new int[] { 0, 3 })]
        public void ParserIntRegTest(string line, int[] result)
        {
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
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
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
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
            Parser p = new Parser();
            var pi = p.ParseLine("test.rasm", line, 1);
            pi.Instructions.Should().HaveCount(1);
            pi.Instructions[0].OpCode.Should().Be(OpCode.INT_RETI);
            pi.Instructions[0].Func2.Should().Be(result[0]);
            pi.Instructions[0].Rd.Should().Be(result[1]);
        }

    }
}
