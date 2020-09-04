using System.Collections.Generic;
using System.Linq;

namespace DockerfileModel
{
    public static class TokenExtensions
    {
        public static IEnumerable<LiteralToken> GetNonCommentLiterals(this IEnumerable<Token> tokens) =>
            tokens.WhereHistory((previousTokens, token) =>
                token is LiteralToken && !IsPrecededByComment(tokens))
                .OfType<LiteralToken>();

        public static IEnumerable<LiteralToken> GetCommentLiterals(this IEnumerable<Token> tokens) =>
            tokens.WhereHistory((previousTokens, token) =>
                token is LiteralToken && IsPrecededByComment(tokens))
                .OfType<LiteralToken>();

        private static bool IsPrecededByComment(IEnumerable<Token> tokens) =>
            tokens.LastOrDefault() is CommentToken ||
                (tokens.LastOrDefault() is WhitespaceToken && tokens.Reverse().Skip(1).FirstOrDefault() is CommentToken);
    }
}
