using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class RunInstruction : Instruction
    {
        private RunInstruction(string text, char escapeChar)
            : base(text, GetParser(escapeChar))
        {
        }

        public RunCommand Command
        {
            get => this.Tokens.OfType<RunCommand>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(Command, value);
            }
        }

        public IEnumerable<MountFlag> MountFlags => Tokens.OfType<MountFlag>();

        public static RunInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new RunInstruction(text, escapeChar);

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Instruction("RUN", escapeChar,
                GetArgsParser(escapeChar));

        public static RunInstruction Create(string command, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            return Parse($"RUN {command}", escapeChar);
        }

        public static RunInstruction Create(IEnumerable<string> commands, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullEmptyOrNullElements(commands, nameof(commands));
            return Parse($"RUN {ExecFormRunCommand.FormatCommands(commands)}", escapeChar);
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of a RUN instruction. It is shell/runtime-specific.
            return ToString();
        }

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            from mounts in ArgTokens(MountFlag.GetParser(escapeChar).AsEnumerable(), escapeChar).Many()
            from whitespace in Whitespace()
            from command in ArgTokens(GetCommandParser(escapeChar).AsEnumerable(), escapeChar)
            select ConcatTokens(
                mounts.Flatten(), whitespace, command);

        private static Parser<RunCommand> GetCommandParser(char escapeChar) =>
            ExecFormRunCommand.GetParser(escapeChar)
                .Cast<ExecFormRunCommand, RunCommand>()
                .XOr(ShellFormRunCommand.GetParser(escapeChar));
    }
}
