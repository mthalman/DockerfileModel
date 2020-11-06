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
        private CommandInstruction(string text, char escapeChar)
            : base(text, GetParser(escapeChar))
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
            new CommandInstruction(text, escapeChar);

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Instruction("CMD", escapeChar,
                GetArgsParser(escapeChar));

        public static CommandInstruction Create(string command, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            return Parse($"CMD {command}", escapeChar);
        }

        public static CommandInstruction Create(IEnumerable<string> commands, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullEmptyOrNullElements(commands, nameof(commands));
            return Parse($"CMD {ExecFormCommand.FormatCommands(commands)}", escapeChar);
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of a CMD instruction. It is shell/runtime-specific.
            return ToString();
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
