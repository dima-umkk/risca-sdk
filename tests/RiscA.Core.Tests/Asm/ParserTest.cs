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
        public void ParseExpressionExceptionTest(string line, string exstr)
        {
            Parser p = new Parser();
            Action act = () => p.ParseLine("test.rasm", line, 1);
            act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
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

        //[Theory]
        //[InlineData("ldi r5, -5+511*(-1)", "%")]
        //public void ParseLDIExceptionTest(string line, string exstr)
        //{
        //    Parser p = new Parser();
        //    Action act = () => p.ParseLine("test.rasm", line, 1);
        //    act.Should().Throw<Exception>().WithMessage($"*{exstr}*");
        //}

    }
}
