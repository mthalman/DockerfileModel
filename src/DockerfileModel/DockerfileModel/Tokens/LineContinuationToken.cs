using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public class LineContinuationToken : AggregateToken
    {
        public LineContinuationToken(char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(Environment.NewLine, escapeChar)
        {
        }

        public LineContinuationToken(string newLine, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens($"{escapeChar}{newLine}", GetInnerParser(escapeChar)))
        {
        }

        internal LineContinuationToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static LineContinuationToken Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new LineContinuationToken(GetTokens(text, GetInnerParser(escapeChar)));

        /// <summary>
        /// Parses a line continuation, consisting of an escape character followed by a new line.
        /// </summary>
        /// <param name="escapeChar">Escape character.</param>
        /// <returns>Line continuation tokens.</returns>
        public static Parser<LineContinuationToken> GetParser(char escapeChar) =>
           from tokens in GetInnerParser(escapeChar)
           select new LineContinuationToken(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
           from escape in Symbol(escapeChar)
           from whitespace in Sprache.Parse.WhiteSpace.Except(Sprache.Parse.LineEnd).Many()
           from lineEnding in Sprache.Parse.LineEnd
           select ConcatTokens(
               escape,
               whitespace.Any() ? new WhitespaceToken(new string(whitespace.ToArray())) : null,
               new NewLineToken(lineEnding));
    }
}
