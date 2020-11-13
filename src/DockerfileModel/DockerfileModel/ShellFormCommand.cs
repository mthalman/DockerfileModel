using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ShellFormCommand : Command
    {
        internal ShellFormCommand(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static ShellFormCommand Create(string command, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(command, escapeChar);

        public static ShellFormCommand Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ShellFormCommand(GetTokens(text, ArgumentListAsLiteral(escapeChar)));

        public static Parser<ShellFormCommand> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in ArgumentListAsLiteral(escapeChar)
            select new ShellFormCommand(tokens);

        public override CommandType CommandType => CommandType.ShellForm;

        public string Value
        {
            get => ValueToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                ValueToken.Value = value;
            }
        }

        public LiteralToken ValueToken
        {
            get => Tokens.OfType<LiteralToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(ValueToken, value);
            }
        }
    }
}
