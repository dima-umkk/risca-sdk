using RiscA.Core.Asm;
using System;
using System.Collections.Generic;
using System.Text;

namespace RiscA.Core.Tests.Asm
{
    public class TokenizerTest
    {
        [Theory]
        [InlineData(" mov r1, r5", new string[] { "mov", "r1", ",", "r5", "{EOL}" } )]
        [InlineData("\tmovi r1, 123", new string[] { "movi", "r1", ",", "123", "{EOL}" })]
        public void testTokenizerStrings(string line, string[] result)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            tokens.Should().NotBeEmpty();
            tokens.Should().HaveCount(result.Length);
            for(int i=0; i<result.Length; i++)
            {
                string expect = result[i];
                tokens[i].TokenString.Should().Be(expect);
            }
        }

        [Theory]
        [InlineData(" mov r1, r5", new TK[] { TK.MOV, TK.REG, TK.COMMA, TK.REG, TK.EOL })]
        [InlineData("\tadd r0, 123", new TK[] { TK.ADD, TK.REG, TK.COMMA, TK.NUMBER, TK.EOL})]
        public void testTokenizerTokenTypes(string line, TK[] result)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            tokens.Should().NotBeEmpty();
            tokens.Should().HaveCount(result.Length);
            for (int i = 0; i < result.Length; i++)
            {
                TK expect = result[i];
                tokens[i].TokenType.Should().Be(expect);
            }
        }

        [Theory]
        [InlineData("add r0, 0xFF", 255)]
        [InlineData("add r0, 0xff", 255)]
        [InlineData("add r0, 0XFF", 255)]
        [InlineData("add r0, 0xABC", 2748)]
        [InlineData("add r0, 0x0", 0)]
        [InlineData("add r0, 0b1010", 10)]
        [InlineData("add r0, 0b0", 0)]
        [InlineData("add r0, 0b11111111", 255)]
        [InlineData("add r0, 0xFFFFFFFF", -1)]
        public void testTokenizerHexBinaryIntValue(string line, int expected)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            tokens.Should().NotBeEmpty();
            var numberToken = tokens.First(t => t.TokenType == TK.NUMBER);
            numberToken.intValue.Should().Be(expected);
        }

        [Theory]
        [InlineData("add r0, 0xFF", new string[] { "add", "r0", ",", "0xFF", "{EOL}" })]
        [InlineData("add r0, 0b1010", new string[] { "add", "r0", ",", "0b1010", "{EOL}" })]
        public void testTokenizerHexBinaryStrings(string line, string[] result)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            tokens.Should().NotBeEmpty();
            tokens.Should().HaveCount(result.Length);
            for (int i = 0; i < result.Length; i++)
            {
                string expect = result[i];
                tokens[i].TokenString.Should().Be(expect);
            }
        }

        [Theory]
        [InlineData("db 'hello'", new string[] { "db", "'hello'", "{EOL}" })]
        [InlineData("db ''", new string[] { "db", "''", "{EOL}" })]
        [InlineData("db 'a b'", new string[] { "db", "'a b'", "{EOL}" })]
        public void testTokenizerStringTokenStrings(string line, string[] result)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            tokens.Should().NotBeEmpty();
            tokens.Should().HaveCount(result.Length);
            for (int i = 0; i < result.Length; i++)
            {
                string expect = result[i];
                tokens[i].TokenString.Should().Be(expect);
            }
        }

        [Theory]
        [InlineData("db 'hello'", new TK[] { TK.DB, TK.STRING, TK.EOL })]
        [InlineData("db ''", new TK[] { TK.DB, TK.STRING, TK.EOL })]
        public void testTokenizerStringTokenTypes(string line, TK[] result)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            tokens.Should().NotBeEmpty();
            tokens.Should().HaveCount(result.Length);
            for (int i = 0; i < result.Length; i++)
            {
                TK expect = result[i];
                tokens[i].TokenType.Should().Be(expect);
            }
        }

        [Theory]
        [InlineData("db 'hello'", "hello")]
        [InlineData("db ''", "")]
        [InlineData("db 'a b'", "a b")]
        [InlineData("db '  '", "  ")]
        public void testTokenizerStringStrValue(string line, string expected)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            tokens.Should().NotBeEmpty();
            var stringToken = tokens.First(t => t.TokenType == TK.STRING);
            stringToken.strValue.Should().Be(expected);
        }

        [Theory]
        [InlineData("db 'hello")]
        [InlineData("db '")]
        public void testTokenizerUnterminatedString(string line)
        {
            Action act = () => Tokenizer.tokenizeLine(line);
            act.Should().Throw<Exception>().WithMessage("*unterminated string literal*");
        }
    }
}
