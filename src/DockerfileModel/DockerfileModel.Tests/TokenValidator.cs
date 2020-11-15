using System;
using System.Linq;
using DockerfileModel.Tokens;
using Xunit;

namespace DockerfileModel.Tests
{
    public static class TokenValidator
    {
        public static void ValidateLineContinuation(Token token, char escapeChar, string newLine) =>
            ValidateAggregate<LineContinuationToken>(token, $"{escapeChar}{newLine}",
                token => ValidateSymbol(token, escapeChar),
                token => ValidateNewLine(token, newLine));

        public static void ValidateWhitespace(Token token, string whitespace)
        {
            Assert.IsType<WhitespaceToken>(token);
            Assert.Equal(whitespace, ((WhitespaceToken)token).Value);
        }

        public static void ValidateSymbol(Token token, char symbol)
        {
            Assert.IsType<SymbolToken>(token);
            Assert.Equal(symbol.ToString(), ((SymbolToken)token).Value);
        }

        public static void ValidateString(Token token, string value)
        {
            Assert.IsType<StringToken>(token);
            StringToken stringToken = (StringToken)token;
            Assert.Equal(value, stringToken.Value);
        }

        public static void ValidateKeyword(Token token, string keyword) =>
            ValidateAggregate<KeywordToken>(token, keyword,
                token => ValidateString(token, keyword));

        public static void ValidateKeyValue(Token token, string key, string value) =>
            ValidateAggregate<KeyValueToken<KeywordToken, LiteralToken>>(token, $"{key}={value}",
                token => ValidateKeyword(token, key),
                token => ValidateSymbol(token, '='),
                token => ValidateLiteral(token, value));

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
