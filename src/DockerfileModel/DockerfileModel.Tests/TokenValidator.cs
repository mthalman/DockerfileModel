using System;
using System.Linq;
using Xunit;

namespace DockerfileModel.Tests
{
    public static class TokenValidator
    {
        public static void ValidateWhitespace(Token token, string whitespace)
        {
            Assert.IsType<WhitespaceToken>(token);
            Assert.Equal(whitespace, token.Value);
        }

        public static void ValidatePunctuation(Token token, string punctuation)
        {
            Assert.IsType<PunctuationToken>(token);
            Assert.Equal(punctuation, token.Value);
        }

        public static void ValidateKeyword(Token token, string keyword)
        {
            Assert.IsType<KeywordToken>(token);
            Assert.Equal(keyword, token.Value);
        }

        public static void ValidateLiteral(Token token, string literal, char? quoteChar = null)
        {
            Assert.IsType<LiteralToken>(token);
            Assert.Equal(literal, token.Value);
            Assert.Equal(quoteChar, ((LiteralToken)token).QuoteChar);
        }

        public static void ValidateIdentifier(Token token, string identifier, char? quoteChar = null)
        {
            Assert.IsType<IdentifierToken>(token);
            Assert.Equal(identifier, token.Value);
            Assert.Equal(quoteChar, ((IdentifierToken)token).QuoteChar);
        }

        public static void ValidateLineContinuation(Token token, string text)
        {
            Assert.IsType<LineContinuationToken>(token);
            Assert.Equal(text, token.Value);
        }

        public static void ValidateNewLine(Token token, string text)
        {
            Assert.IsType<NewLineToken>(token);
            Assert.Equal(text, token.Value);
        }

        public static void ValidateAggregate<T>(Token token, string text, params Action<Token>[] tokenValidators)
            where T : AggregateToken
        {
            Assert.IsType<T>(token);
            Assert.Equal(text, token.ToString());

            if (tokenValidators.Any())
            {
                Assert.Collection(((T)token).Tokens, tokenValidators);
            }
        }
    }
}
