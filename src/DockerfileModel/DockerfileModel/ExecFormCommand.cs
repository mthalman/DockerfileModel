using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ExecFormCommand : Command
    {
        internal ExecFormCommand(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static ExecFormCommand Create(IEnumerable<string> commands, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullEmptyOrNullElements(commands, nameof(commands));
            return Parse(StringHelper.FormatAsJson(commands), escapeChar);
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
