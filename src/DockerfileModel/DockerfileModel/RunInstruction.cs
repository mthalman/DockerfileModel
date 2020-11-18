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
        private readonly ProjectedItemList<MountFlag, Mount> mounts;

        private RunInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
            this.mounts = new ProjectedItemList<MountFlag, Mount>(
                new TokenList<MountFlag>(TokenList),
                flag => flag.ValueToken,
                (flag, mount) => flag.ValueToken = mount);
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

        public IList<Mount> Mounts => this.mounts;

        public static RunInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new RunInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<RunInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new RunInstruction(tokens);

        public static RunInstruction Create(string command, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Create(command, Enumerable.Empty<Mount>(), escapeChar);

        public static RunInstruction Create(string command, IEnumerable<Mount> mounts, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            Requires.NotNull(mounts, nameof(mounts));

            return Parse($"RUN {CreateMountFlagArgs(mounts)}{command}", escapeChar);
        }

        public static RunInstruction Create(IEnumerable<string> commands, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Create(commands, Enumerable.Empty<Mount>(), escapeChar);

        public static RunInstruction Create(IEnumerable<string> commands, IEnumerable<Mount> mounts, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullEmptyOrNullElements(commands, nameof(commands));
            Requires.NotNull(mounts, nameof(mounts));

            return Parse($"RUN {CreateMountFlagArgs(mounts)}{StringHelper.FormatAsJson(commands)}", escapeChar);
        }

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of a RUN instruction. It is shell/runtime-specific.
            return ToString();
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Instruction("RUN", escapeChar,
                GetArgsParser(escapeChar));

        private static string CreateMountFlagArgs(IEnumerable<Mount> mounts)
        {
            if (!mounts.Any())
            {
                return String.Empty;
            }

            return $"{String.Join(" ", mounts.Select(mount => MountFlag.Create(mount).ToString()).ToArray())} ";
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
