using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Validation;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel
{
    public class ExecFormCommand : Command
    {
        public ExecFormCommand(IEnumerable<string> values, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(values, escapeChar))
        {
        }

        internal ExecFormCommand(IEnumerable<Token> tokens) : base(tokens)
        {
            ValueTokens = new TokenList<LiteralToken>(TokenList);
            Values = new ProjectedItemList<LiteralToken, string>(
                ValueTokens,
                token => token.Value,
                (token, value) => token.Value = value);
        }

        private static IEnumerable<Token> GetTokens(IEnumerable<string> values, char escapeChar)
        {
            Requires.NotNullEmptyOrNullElements(values, nameof(values));
            return GetTokens(StringHelper.FormatAsJson(values), GetInnerParser(escapeChar));
        }

        public static ExecFormCommand Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ExecFormCommand(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<ExecFormCommand> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ExecFormCommand(tokens);

        public IList<string> Values { get; }

        public IList<LiteralToken> ValueTokens { get; }

        public override CommandType CommandType => CommandType.ExecForm;

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            ArgTokens(JsonArray(escapeChar, canContainVariables: false), escapeChar);
    }
}
