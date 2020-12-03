using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class StageName : IdentifierToken
    {
        private readonly char escapeChar;

        internal StageName(IEnumerable<Token> tokens, char escapeChar) : base(tokens)
        {
            this.escapeChar = escapeChar;
        }

        public StageName(string value, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(value, GetInnerParser(escapeChar)), escapeChar)
        {
        }

        public static Parser<StageName> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new StageName(tokens, escapeChar);

        protected override IEnumerable<Token> GetInnerTokens(string value) =>
            GetTokens(value, GetInnerParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            IdentifierString(escapeChar, FirstCharParser(), TailCharParser());

        private static Parser<char> FirstCharParser() => Sprache.Parse.Letter;

        private static Parser<char> TailCharParser() =>
            Sprache.Parse.LetterOrDigit
                .Or(Sprache.Parse.Char('_'))
                .Or(Sprache.Parse.Char('-'))
                .Or(Sprache.Parse.Char('.'));
    }
}
