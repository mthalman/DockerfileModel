using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public class LineContinuationToken : AggregateToken
    {
        internal LineContinuationToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static LineContinuationToken Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new LineContinuationToken(GetTokens(text, GetInnerParser(escapeChar)));

        public static LineContinuationToken Create(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Create(Environment.NewLine, escapeChar);

        public static LineContinuationToken Create(string newLine, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse($"{escapeChar}{newLine}", escapeChar);

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
