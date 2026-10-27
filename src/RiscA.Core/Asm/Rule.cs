using System.Collections.Generic;

namespace RiscA.Core.Asm
{
    /// <summary>
    /// Handler of a pattern rule: it gets the pattern that matched, the token stack and the
    /// position where the pattern matched, rewrites the stack and returns it. Returning
    /// <c>null</c> means "the shape matched, but this rule does not apply" - the engine
    /// (<see cref="Parser.ProcessToken{TCtx}"/>) then keeps looking for another rule.
    /// </summary>
    /// <typeparam name="TCtx">what the rule builds up while the line is reduced</typeparam>
    public delegate List<Token>? RuleHandler<TCtx>(TCtx context, List<TK[]> pattern, List<Token> tokens, int pos);
}
