using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class EntrypointInstruction : Instruction
    {
        public EntrypointInstruction(string commandWithArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(commandWithArgs, escapeChar))
        {
        }

        public EntrypointInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(command, args, escapeChar))
        {
        }

        private EntrypointInstruction(IEnumerable<Token> tokens) : base(tokens)
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

        public static EntrypointInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new EntrypointInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<EntrypointInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new EntrypointInstruction(tokens);

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of an ENTRYPOINT instruction. It is shell/runtime-specific.
            return ToString();
        }

        private static IEnumerable<Token> GetTokens(string commandWithArgs, char escapeChar)
        {
            Requires.NotNullOrEmpty(commandWithArgs, nameof(commandWithArgs));
            return GetTokens($"ENTRYPOINT {commandWithArgs}", GetInnerParser(escapeChar));
        }

        private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, char escapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            Requires.NotNull(args, nameof(args));
            return GetTokens($"ENTRYPOINT {StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Instruction("ENTRYPOINT", escapeChar,
                GetArgsParser(escapeChar));

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
