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
        private GenericInstruction(string text, char escapeChar)
            : base(GetTokens(text, InstructionParser(escapeChar)))
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
            return InstructionParser(escapeChar).TryParse(text).WasSuccessful;
        }

        public static GenericInstruction Create(string instruction, string args, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullOrEmpty(instruction, nameof(instruction));
            return Parse($"{instruction} {args}", escapeChar);
        }

        public static GenericInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new GenericInstruction(text, escapeChar);

        private static Parser<IEnumerable<Token>> InstructionParser(char escapeChar) =>
            from leading in Whitespace()
            from instruction in TokenWithTrailingWhitespace(DockerfileParser.InstructionIdentifier(escapeChar))
            from lineContinuation in LineContinuationToken.GetParser(escapeChar).Optional()
            from instructionArgs in InstructionArgs(escapeChar)
            select ConcatTokens(leading, instruction, new Token[] { lineContinuation.GetOrDefault() }, instructionArgs);
    }
}
