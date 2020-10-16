using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class Instruction : InstructionBase
    {
        public const char DefaultEscapeChar = '\\';

        private Instruction(string text, char escapeChar)
            : base(text, InstructionParser(escapeChar))
        {
        }

        public IList<string> ArgLines =>
            new StringWrapperList<LiteralToken>(
                Tokens.OfType<LiteralToken>(),
                token => token.Value,
                (token, value) => token.Value = value);

        public static bool IsInstruction(string text, char escapeChar) =>
            InstructionParser(escapeChar).TryParse(text).WasSuccessful;

        public static Instruction Create(string instruction, string args) =>
            Parse($"{instruction} {args}", DefaultEscapeChar);

        public static Instruction Parse(string text, char escapeChar) =>
            new Instruction(text, escapeChar);

        private static Parser<IEnumerable<Token>> InstructionParser(char escapeChar) =>
            from leading in Whitespace()
            from instruction in TokenWithTrailingWhitespace(DockerfileParser.InstructionIdentifier())
            from lineContinuation in LineContinuation(escapeChar).Optional()
            from instructionArgs in InstructionArgs(escapeChar)
            select ConcatTokens(leading, instruction, new Token[] { lineContinuation.GetOrDefault() }, instructionArgs);
    }
}
