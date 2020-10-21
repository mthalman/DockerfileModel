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

        public static void ValidateString(Token token, string literal)
        {
            Assert.IsType<StringToken>(token);
            StringToken literalToken = (StringToken)token;
            Assert.Equal(literal, literalToken.Value);
        }

        public static void ValidateKeyword(Token token, string keyword) =>
            ValidateAggregate<KeywordToken>(token, keyword,
                token => ValidateString(token, keyword));

        public static void ValidateLiteral(Token token, string literal, char? quoteChar = null)
        {
            Action<Token>[] validators = !String.IsNullOrEmpty(literal) ?
                new Action<Token>[] { token => ValidateString(token, literal) } :
                Array.Empty<Action<Token>>();
            ValidateQuotableAggregate<LiteralToken>(token, $"{quoteChar}{literal}{quoteChar}", quoteChar, validators);
        }
            
        public static void ValidateIdentifier(Token token, string identifier, char? quoteChar = null) =>
            ValidateQuotableAggregate<IdentifierToken>(token, $"{quoteChar}{identifier}{quoteChar}", quoteChar,
                token => ValidateString(token, identifier));

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

        public static void ValidateQuotableAggregate<T>(Token token, string text, char? quoteChar, params Action<Token>[] tokenValidators)
            where T : AggregateToken, IQuotableToken
        {
            Assert.IsType<T>(token);
            Assert.Equal(quoteChar, ((T)token).QuoteChar);
            ValidateAggregate<T>(token, text, tokenValidators);
        }
    }
}
