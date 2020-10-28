using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ShellFormRunCommand : RunCommand
    {
        private ShellFormRunCommand(string text, char escapeChar)
            : base(text, GetInnerParser(escapeChar))
        {
        }

        internal ShellFormRunCommand(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static ShellFormRunCommand Create(string command) =>
            Parse(command, Instruction.DefaultEscapeChar);

        public static ShellFormRunCommand Parse(string text, char escapeChar) =>
            new ShellFormRunCommand(text, escapeChar);

        public static Parser<ShellFormRunCommand> GetParser(char escapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ShellFormRunCommand(tokens);

        public override RunCommandType CommandType => RunCommandType.ShellForm;

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

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            from literals in ArgTokens(
                LiteralToken(escapeChar, new char[0]).AsEnumerable(),
                escapeChar).Many()
            select CollapseRunCommandTokens(literals.Flatten());
    }
}
