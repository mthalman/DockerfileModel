using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ShellInstruction : Instruction
    {
        public ShellInstruction(string command, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(command, Enumerable.Empty<string>(), escapeChar)
        {
        }

        public ShellInstruction(string command, IEnumerable<string> args, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(command, args, escapeChar))
        {
        }

        private ShellInstruction(IEnumerable<Token> tokens) : base(tokens)
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

        public static ShellInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ShellInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<ShellInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ShellInstruction(tokens);

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of an ENTRYPOINT instruction. It is shell/runtime-specific.
            return ToString();
        }

        private static IEnumerable<Token> GetTokens(string command, IEnumerable<string> args, char escapeChar)
        {
            Requires.NotNull(command, nameof(command));
            Requires.NotNull(args, nameof(args));
            return GetTokens($"SHELL {StringHelper.FormatAsJson(new string[] { command }.Concat(args))}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Instruction("SHELL", escapeChar,
                GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            ArgTokens(ExecFormCommand.GetParser(escapeChar).AsEnumerable(), escapeChar, excludeTrailingWhitespace: true);
    }
}
