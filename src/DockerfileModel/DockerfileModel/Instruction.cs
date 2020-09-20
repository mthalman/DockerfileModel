using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public class Instruction : InstructionBase
    {
        public const char DefaultEscapeChar = '\\';

        private Instruction(string text, char escapeChar)
            : base(text, DockerfileParser.Instruction(escapeChar))
        {
        }

        public IList<string> ArgLines =>
            new StringWrapperList<LiteralToken>(
                Tokens.OfType<LiteralToken>(),
                token => token.Value,
                (token, value) => token.Value = value);

        public static bool IsInstruction(string text, char escapeChar) =>
            DockerfileParser.Instruction(escapeChar).TryParse(text).WasSuccessful;

        public static Instruction Create(string instruction, string args) =>
            Parse($"{instruction} {args}", DefaultEscapeChar);

        public static Instruction Parse(string text, char escapeChar) =>
            new Instruction(text, escapeChar);
    }
}
