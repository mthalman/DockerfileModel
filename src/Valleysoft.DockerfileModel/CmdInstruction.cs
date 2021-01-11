using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Validation;
using static Valleysoft.DockerfileModel.ParseHelper;

namespace Valleysoft.DockerfileModel
{
    public class CmdInstruction : Instruction
    {
        public CmdInstruction(string commandWithArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(commandWithArgs, escapeChar))
        {
        }

        public CmdInstruction(IEnumerable<string> defaultArgs, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(defaultArgs, escapeChar))
        {
        }

        public CmdInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(command, args, escapeChar))
        {
        }

        private CmdInstruction(IEnumerable<Token> tokens) : base(tokens)
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

        public static CmdInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new CmdInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<CmdInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new CmdInstruction(tokens);

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of a CMD instruction. It is shell/runtime-specific.
            return ToString();
        }

        internal static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Instruction("CMD", escapeChar, GetArgsParser(escapeChar));

        private static IEnumerable<Token> GetTokens(string commandWithArgs, char escapeChar)
        {
            Requires.NotNullOrEmpty(commandWithArgs, nameof(commandWithArgs));
            return GetTokens($"CMD {commandWithArgs}", GetInnerParser(escapeChar));
        }

        private static IEnumerable<Token> GetTokens(IEnumerable<string> defaultArgs, char escapeChar)
        {
            Requires.NotNullEmptyOrNullElements(defaultArgs, nameof(defaultArgs));
            return GetTokens($"CMD {StringHelper.FormatAsJson(defaultArgs)}", GetInnerParser(escapeChar));
        }

        private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, char escapeChar)
        {
            Requires.NotNullOrEmpty(command, nameof(command));
            Requires.NotNull(args, nameof(args));
            return GetTokens($"CMD {StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
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
