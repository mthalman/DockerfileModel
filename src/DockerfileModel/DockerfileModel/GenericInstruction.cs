using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class GenericInstruction : Instruction
    {
        public GenericInstruction(string instruction, string args, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(instruction, args, escapeChar))
        {
            
        }

        private GenericInstruction(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public IList<string> ArgLines =>
            new ProjectedItemList<LiteralToken, string>(
                Tokens.OfType<LiteralToken>(),
                token => token.Value,
                (token, value) => token.Value = value);

        internal static bool IsInstruction(string text, char escapeChar)
        {
            Requires.NotNull(text, nameof(text));
            return GetInnerParser(escapeChar).TryParse(text).WasSuccessful;
        }

        public static GenericInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new GenericInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        private static IEnumerable<Token> GetTokens(string instruction, string args, char escapeChar)
        {
            Requires.NotNullOrEmpty(instruction, nameof(instruction));
            Requires.NotNullOrEmpty(args, nameof(args));
            return GetTokens($"{instruction} {args}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            from leading in Whitespace()
            from instruction in TokenWithTrailingWhitespace(DockerfileParser.InstructionIdentifier(escapeChar))
            from lineContinuation in LineContinuations(escapeChar).Optional()
            from instructionArgs in InstructionArgs(escapeChar)
            select ConcatTokens(leading, instruction, lineContinuation.GetOrDefault(), instructionArgs);
    }
}
