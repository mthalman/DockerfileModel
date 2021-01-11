using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Validation;

using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel
{
    public class ShellFormCommand : Command
    {
        public ShellFormCommand(string command, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(command, escapeChar))
        {
        }

        internal ShellFormCommand(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static ShellFormCommand Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ShellFormCommand(GetTokens(text, ArgumentListAsLiteral(escapeChar)));

        public static Parser<ShellFormCommand> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
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

        private static IEnumerable<Token> GetTokens(string command, char escapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            return GetTokens(command, GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            ArgumentListAsLiteral(escapeChar);
    }
}
