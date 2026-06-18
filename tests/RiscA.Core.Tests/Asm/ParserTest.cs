using RiscA.Core.Asm;
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
        public void ParserLineTest(string line, int[] result)
        {
            Parser p = new Parser();
            p.ParseLine("test.rasm", line, 1);
            p.AsmInstructions.Should().HaveCount(1);
            p.AsmInstructions[0].Instruction.Func3.Should().Be(result[0]);
            p.AsmInstructions[0].Instruction.Rd.Should().Be(result[1]);
            p.AsmInstructions[0].Instruction.Rs.Should().Be(result[2]);
        }
    }
}
