using Xunit;

namespace DockerfileModel.Tests
{
    public static class TokenValidator
    {
        public static void ValidateOperator(Token token)
        {
            Assert.IsType<OperatorToken>(token);
            Assert.Equal("=", token.Value);
        }

        public static void ValidateWhitespace(Token token, string whitespace)
        {
            Assert.IsType<WhitespaceToken>(token);
            Assert.Equal(whitespace, token.Value);
        }

        public static void ValidateComment(Token token)
        {
            Assert.IsType<CommentToken>(token);
            Assert.Equal("#", token.Value);
        }

        public static void ValidateKeyword(Token token, string keyword)
        {
            Assert.IsType<KeywordToken>(token);
            Assert.Equal(keyword, token.Value);
        }

        public static void ValidateLiteral(Token token, string literal)
        {
            Assert.IsType<LiteralToken>(token);
            Assert.Equal(literal, token.Value);
        }

        public static void ValidateCommentText(Token token, string text)
        {
            Assert.IsType<CommentTextToken>(token);
            Assert.Equal(text, token.Value);
        }

        public static void ValidateLineContinuation(Token token, string text)
        {
            Assert.IsType<LineContinuationToken>(token);
            Assert.Equal(text, token.Value);
        }
    }
}
