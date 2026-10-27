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

        //a '.name' is a single token so that the preprocessor can match its directives with
        //the same token patterns the parser uses for instructions
        [Theory]
        [InlineData(".equ A, 1", new TK[] { TK.EQU, TK.LITERAL, TK.COMMA, TK.NUMBER, TK.EOL })]
        [InlineData(".include \"lib/uart.rasm\"", new TK[] { TK.INCLUDE, TK.STRING, TK.EOL })]
        [InlineData(".func main:", new TK[] { TK.FUNC, TK.LITERAL, TK.COLON, TK.EOL })]
        [InlineData(".EQU A, 1", new TK[] { TK.EQU, TK.LITERAL, TK.COMMA, TK.NUMBER, TK.EOL })]
        [InlineData(".equiv A, 1", new TK[] { TK.DOTNAME, TK.LITERAL, TK.COMMA, TK.NUMBER, TK.EOL })]
        public void testTokenizerDirectiveTokenTypes(string line, TK[] result)
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
        [InlineData(".equiv A, 1", ".equiv")]
        [InlineData(".macro x", ".macro")]
        public void testTokenizerUnknownDirectiveKeepsItsName(string line, string expected)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            var dotName = tokens.First(t => t.TokenType == TK.DOTNAME);
            dotName.TokenString.Should().Be(expected);
        }

        //double quotes are read too - '.include "file"' is written with them; the STRING token
        //carries the content without the quotes, like the single quoted form does
        [Theory]
        [InlineData(".include \"lib/uart.rasm\"", new TK[] { TK.INCLUDE, TK.STRING, TK.EOL })]
        [InlineData("db \"hello\"", new TK[] { TK.DB, TK.STRING, TK.EOL })]
        public void testTokenizerDoubleQuotedStringTypes(string line, TK[] result)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            tokens.Should().HaveCount(result.Length);
            for (int i = 0; i < result.Length; i++)
                tokens[i].TokenType.Should().Be(result[i]);
        }

        [Theory]
        [InlineData(".include \"lib/uart.rasm\"", "lib/uart.rasm")]
        [InlineData("\"\"", "")]
        public void testTokenizerDoubleQuotedStrValue(string line, string expected)
        {
            var tokens = Tokenizer.tokenizeLine(line);
            var stringToken = tokens.First(t => t.TokenType == TK.STRING);
            stringToken.strValue.Should().Be(expected);
        }

        [Theory]
        [InlineData(".include \"lib")]
        [InlineData("db \"")]
        public void testTokenizerUnterminatedDoubleQuotedString(string line)
        {
            Action act = () => Tokenizer.tokenizeLine(line);
            act.Should().Throw<Exception>().WithMessage("*unterminated string literal*");
        }
    }
}
