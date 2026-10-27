using System;
using System.Collections.Generic;

namespace RiscA.Core.Asm
{
    /// <summary>
    /// The number-expression rules shared by every pass that reduces a line: the parser and the
    /// preprocessor both spread <see cref="Rules{TCtx}"/> at the head of their rule tables, so an
    /// expression means the same thing everywhere - '1 + 2 * 3' is 7 in an immediate and in an
    /// '.equ' value - and the two tables cannot drift apart. <see cref="Skip{TCtx}"/> lives here
    /// too: both tables drop their leftover EOL with the same one-line handler.
    /// </summary>
    public static class Expressions
    {
        //the handlers are generic over the pass context, so each rule table takes the rules
        //instantiated for its own TCtx; multiply/divide is listed before add/subtract because
        //the engine applies the earliest rule that matches, giving '1 + 2 * 3' the value 7
        public static List<(List<TK[]> pattern, RuleHandler<TCtx> handler)> Rules<TCtx>() =>
            [
                ([ [TK.MINUS], [TK.NUMBER] ], ExpNegative<TCtx>),
                ([ [TK.LPAREN], [TK.NUMBER], [TK.RPAREN] ], ExpParen<TCtx>),
                ([ [TK.NUMBER], [TK.ASTER, TK.SLASH], [TK.NUMBER] ], ExpMath<TCtx>),
                ([ [TK.NUMBER], [TK.MINUS], [TK.NUMBER] ], ExpMath<TCtx>),
                ([ [TK.NUMBER], [TK.PLUS], [TK.NUMBER] ], ExpMath<TCtx>),
            ];

        static List<Token>? ExpMath<TCtx>(TCtx context, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            int a = tokens[pos + 0].intValue;
            int b = tokens[pos + 2].intValue;
            int c = tokens[pos + 1].TokenType switch
            {
                TK.PLUS => a + b,
                TK.MINUS => a - b,
                TK.ASTER => a * b,
                //reported explicitly instead of letting int division throw DivideByZeroException:
                //the message then keeps naming the expression the user actually wrote
                TK.SLASH => b == 0
                    ? throw new Exception($"Division by zero in '{tokens[pos].TokenString} {tokens[pos + 1].TokenString} {tokens[pos + 2].TokenString}'")
                    : a / b,
                _ => throw new Exception($"Unexpected math operation '{tokens[pos + 1].TokenString}' in '{tokens[pos].TokenString} {tokens[pos + 1].TokenString} {tokens[pos + 2].TokenString}'")
            };
            tokens.RemoveRange(pos, 3);
            tokens.Insert(pos, new Token(TK.NUMBER, c.ToString(), pos, c));
            return tokens;
        }

        static List<Token>? ExpParen<TCtx>(TCtx context, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            Token number = tokens[pos + 1];
            tokens.RemoveRange(pos, 3);
            tokens.Insert(pos, number);
            return tokens;
        }

        //a MINUS followed by a NUMBER is unary unless a value stands in front of it, where the
        //binary minus rule already claims it
        static List<Token>? ExpNegative<TCtx>(TCtx context, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            if (pos > 0 && tokens[pos - 1].TokenType is TK.NUMBER or TK.RPAREN)
                return null;
            Token number = tokens[pos + 1];
            number.intValue = 0 - number.intValue;
            number.TokenString = number.intValue.ToString();
            tokens.RemoveRange(pos, 2);
            tokens.Insert(pos, number);
            return tokens;
        }

        //drop the matched pattern: the skip rules at the tail of both tables
        public static List<Token>? Skip<TCtx>(TCtx context, List<TK[]> pattern, List<Token> tokens, int pos)
        {
            tokens.RemoveRange(pos, pattern.Count);
            return tokens;
        }
    }
}
