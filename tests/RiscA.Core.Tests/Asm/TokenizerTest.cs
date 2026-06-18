using RiscA.Core.Asm;
using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Core.Tests.Asm
{
    public class TokenizerTest
    {
        [Theory]
        [InlineData(" mov r1, r5", new string[] { "mov", "r1", ",", "r5" } )]
        [InlineData("\tmovi r1, 123", new string[] { "movi", "r1", ",", "123" })]
        public void testTokenizerStrings(string line, string[] result)
        {
            var tokens = Tokenizer.tokenizeLine("test.rasm", line, 1);
            tokens.Should().NotBeEmpty();
            tokens.Should().HaveCount(result.Length);
            for(int i=0; i<result.Length; i++)
            {
                string expect = result[i];
                tokens[i].TokenString.Should().Be(expect);
            }
        }

        [Theory]
        [InlineData(" mov r1, r5", new TK[] { TK.MOV, TK.REG, TK.COMMA, TK.REG })]
        [InlineData("\tadd r0, 123", new TK[] { TK.ADD, TK.REG, TK.COMMA, TK.NUMBER})]
        public void testTokenizerTokenTypes(string line, TK[] result)
        {
            var tokens = Tokenizer.tokenizeLine("test.rasm", line, 1);
            tokens.Should().NotBeEmpty();
            tokens.Should().HaveCount(result.Length);
            for (int i = 0; i < result.Length; i++)
            {
                TK expect = result[i];
                tokens[i].TokenType.Should().Be(expect);
            }
        }
    }
}
