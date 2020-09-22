using System;
using System.Linq;
using DockerfileModel.Tokens;
using Xunit;

namespace DockerfileModel.Tests
{
    public static class TokenValidator
    {
        public static void ValidateWhitespace(Token token, string whitespace)
        {
            Assert.IsType<WhitespaceToken>(token);
            Assert.Equal(whitespace, ((WhitespaceToken)token).Value);
        }

        public static void ValidateSymbol(Token token, string symbol)
        {
            Assert.IsType<SymbolToken>(token);
            Assert.Equal(symbol, ((SymbolToken)token).Value);
        }

        public static void ValidateKeyword(Token token, string keyword)
        {
            Assert.IsType<KeywordToken>(token);
            Assert.Equal(keyword, ((KeywordToken)token).Value);
        }

        public static void ValidateLiteral(Token token, string literal, char? quoteChar = null)
        {
            Assert.IsType<LiteralToken>(token);
            LiteralToken literalToken = (LiteralToken)token;
            Assert.Equal(literal, literalToken.Value);
            Assert.Equal(quoteChar, literalToken.QuoteChar);
        }

        public static void ValidateIdentifier(Token token, string identifier, char? quoteChar = null)
        {
            Assert.IsType<IdentifierToken>(token);
            IdentifierToken identifierToken = (IdentifierToken)token;
            Assert.Equal(identifier, identifierToken.Value);
            Assert.Equal(quoteChar, identifierToken.QuoteChar);
        }

        public static void ValidateLineContinuation(Token token, string text)
        {
            Assert.IsType<LineContinuationToken>(token);
            Assert.Equal(text, ((LineContinuationToken)token).Value);
        }

        public static void ValidateNewLine(Token token, string text)
        {
            Assert.IsType<NewLineToken>(token);
            Assert.Equal(text, ((NewLineToken)token).Value);
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
