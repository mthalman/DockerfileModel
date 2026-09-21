using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Identifies nested formatting that cannot be treated as freely replaceable operand text.</summary>
internal static class NestedEditTrivia
{
    /// <summary>Enumerates trivia containers without descending into quoted payloads or already-selected trivia.</summary>
    /// <param name="token">The live subtree to inspect.</param>
    /// <returns>Existing comment, continuation, and whitespace tokens in traversal order.</returns>
    /// <remarks>Returned tokens retain their identities; callers must stage any transfer of ownership.</remarks>
    internal static IEnumerable<Token> Collect(Token token)
    {
        if (token is CommentToken or LineContinuationToken or WhitespaceToken)
        {
            yield return token;
        }
        else if (token is AggregateToken aggregate && token is not IQuotableToken { QuoteChar: not null })
        {
            foreach (Token child in aggregate.Tokens)
                foreach (Token trivia in Collect(child))
                    yield return trivia;
        }
    }

    /// <summary>Rejects preserving replacement when a subtree contains comments or line continuations.</summary>
    /// <param name="token">The payload selected for replacement or removal.</param>
    /// <param name="trivia">The already-validated trivia policy.</param>
    /// <remarks>Unlike trivia collection, this conservative guard also inspects quoted descendants.</remarks>
    internal static void RequireDiscardIfEmbedded(Token token, TriviaDisposition trivia)
    {
        if (trivia == TriviaDisposition.Preserve && HasEmbedded(token))
            throw new InvalidOperationException("Embedded trivia cannot safely survive this replacement; explicitly discard it.");
    }

    private static bool HasEmbedded(Token token) => token is CommentToken or LineContinuationToken ||
        token is AggregateToken aggregate && aggregate.Tokens.Any(HasEmbedded);
}
