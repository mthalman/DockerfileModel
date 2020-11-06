using System;
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

        public Command Command
        {
            get => this.Tokens.OfType<Command>().First();
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

        public static RunInstruction Create(string command, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Create(command, Enumerable.Empty<MountFlag>(), escapeChar);

        public static RunInstruction Create(string command, IEnumerable<MountFlag> mountFlags, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            Requires.NotNull(mountFlags, nameof(mountFlags));

            return Parse($"RUN {CreateMountFlagArgs(mountFlags)}{command}", escapeChar);
        }

        public static RunInstruction Create(IEnumerable<string> commands, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Create(commands, Enumerable.Empty<MountFlag>(), escapeChar);

        public static RunInstruction Create(IEnumerable<string> commands, IEnumerable<MountFlag> mountFlags, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullEmptyOrNullElements(commands, nameof(commands));
            Requires.NotNull(mountFlags, nameof(mountFlags));

            return Parse($"RUN {CreateMountFlagArgs(mountFlags)}{ExecFormCommand.FormatCommands(commands)}", escapeChar);
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of a RUN instruction. It is shell/runtime-specific.
            return ToString();
        }

        private static string CreateMountFlagArgs(IEnumerable<MountFlag> mountFlags)
        {
            if (!mountFlags.Any())
            {
                return String.Empty;
            }

            return $"{String.Join(" ", mountFlags.Select(flag => flag.ToString()).ToArray())} ";
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
