using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class CommandInstruction : Instruction
    {
        public CommandInstruction(string command, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(command, escapeChar))
        {
        }

        public CommandInstruction(IEnumerable<string> commands, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(commands, escapeChar))
        {
        }

        private CommandInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public Command Command
        {
            get => this.Tokens.OfType<Command>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(Command, value);
            }
        }

        public static CommandInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new CommandInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<CommandInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new CommandInstruction(tokens);

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of a CMD instruction. It is shell/runtime-specific.
            return ToString();
        }

        internal static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Instruction("CMD", escapeChar,
                GetArgsParser(escapeChar));

        private static IEnumerable<Token> GetTokens(string command, char escapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            return GetTokens($"CMD {command}", GetInnerParser(escapeChar));
        }

        private static IEnumerable<Token> GetTokens(IEnumerable<string> commands, char escapeChar)
        {
            Requires.NotNullEmptyOrNullElements(commands, nameof(commands));
            return GetTokens($"CMD {StringHelper.FormatAsJson(commands)}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            from mounts in ArgTokens(MountFlag.GetParser(escapeChar).AsEnumerable(), escapeChar).Many()
            from whitespace in Whitespace()
            from command in ArgTokens(GetCommandParser(escapeChar).AsEnumerable(), escapeChar)
            select ConcatTokens(
                mounts.Flatten(), whitespace, command);

        private static Parser<Command> GetCommandParser(char escapeChar) =>
            ExecFormCommand.GetParser(escapeChar)
                .Cast<ExecFormCommand, Command>()
                .XOr(ShellFormCommand.GetParser(escapeChar));
    }
}
