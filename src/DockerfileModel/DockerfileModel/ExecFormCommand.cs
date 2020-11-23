using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ExecFormCommand : Command
    {
        public ExecFormCommand(IEnumerable<string> commands, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(commands, escapeChar))
        {
        }

        internal ExecFormCommand(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        private static IEnumerable<Token> GetTokens(IEnumerable<string> commands, char escapeChar)
        {
            Requires.NotNullEmptyOrNullElements(commands, nameof(commands));
            return GetTokens(StringHelper.FormatAsJson(commands), GetInnerParser(escapeChar));
        }

        public static ExecFormCommand Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ExecFormCommand(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<ExecFormCommand> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ExecFormCommand(tokens);

        public IList<string> CommandArgs =>
            new ProjectedItemList<LiteralToken, string>(
                CommandArgTokens,
                token => token.Value,
                (token, value) => token.Value = value);

        public IEnumerable<LiteralToken> CommandArgTokens => Tokens.OfType<LiteralToken>();

        public override CommandType CommandType => CommandType.ExecForm;

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            JsonArray(escapeChar, canContainVariables: false);
    }
}
