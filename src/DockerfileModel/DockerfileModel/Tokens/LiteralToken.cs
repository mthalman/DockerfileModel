using System.Collections.Generic;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel.Tokens
{
    public class LiteralToken : AggregateToken, IQuotableValueToken
    {
        public LiteralToken(string value)
             : base(new Token[] { new StringToken(value) })
        {
        }

        internal LiteralToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Value
        {
            get => this.ToString(TokenStringOptions.CreateOptionsForValueString());
            set
            {
                Requires.NotNull(value, nameof(value));
                ReplaceWithToken(new StringToken(value));
            }
        }

        public char? QuoteChar { get; set; }

        public static LiteralToken Parse(string text, bool canContainVariables = false, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Parser<LiteralToken> parser;
            if (canContainVariables)
            {
                parser = LiteralAggregate(escapeChar, whitespaceMode: WhitespaceMode.Allowed);
            }
            else
            {
                parser = WrappedInOptionalQuotesLiteralStringWithSpaces(escapeChar, excludeVariableRefChars: false);
            }

            return parser.Parse(text);
        }
    }
}
