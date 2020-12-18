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
        public RunInstruction(string commandWithArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(commandWithArgs, Enumerable.Empty<Mount>(), escapeChar)
        {
        }

        public RunInstruction(string commandWithArgs, IEnumerable<Mount> mounts, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(commandWithArgs, mounts, escapeChar))
        {
        }

        public RunInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(command, args, Enumerable.Empty<Mount>(), escapeChar)
        {
        }

        public RunInstruction(string command, IEnumerable<string> args, IEnumerable<Mount> mounts, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(command, args, mounts, escapeChar))
        {
        }

        private RunInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
            Mounts = new ProjectedItemList<MountFlag, Mount>(
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

        public IList<Mount> Mounts { get; }

        public static RunInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new RunInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<RunInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new RunInstruction(tokens);

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of a RUN instruction. It is shell/runtime-specific.
            return ToString();
        }

        private static IEnumerable<Token> GetTokens(string commandWithArgs, IEnumerable<Mount> mounts, char escapeChar)
        {
            Requires.NotNullOrEmpty(commandWithArgs, nameof(commandWithArgs));
            Requires.NotNull(mounts, nameof(mounts));

            return GetTokens($"RUN {CreateMountFlagArgs(mounts, escapeChar)}{commandWithArgs}", GetInnerParser(escapeChar));
        }

        private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, IEnumerable<Mount> mounts, char escapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            Requires.NotNull(args, nameof(args));
            Requires.NotNull(mounts, nameof(mounts));

            return GetTokens(
                $"RUN {CreateMountFlagArgs(mounts, escapeChar)}{StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Instruction("RUN", escapeChar,
                GetArgsParser(escapeChar));

        private static string CreateMountFlagArgs(IEnumerable<Mount> mounts, char escapeChar)
        {
            if (!mounts.Any())
            {
                return String.Empty;
            }

            return $"{String.Join(" ", mounts.Select(mount => new MountFlag(mount, escapeChar).ToString()).ToArray())} ";
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
                .Or(ShellFormCommand.GetParser(escapeChar));
    }
}
