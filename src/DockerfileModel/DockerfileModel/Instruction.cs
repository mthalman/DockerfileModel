using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public class Instruction : DockerfileLine
    {
        public const char DefaultEscapeChar = '\\';

        private Instruction(string text, char escapeChar)
            : base(text, DockerfileParser.Instruction(escapeChar))
        {
        }

        public KeywordToken InstructionName => Tokens.OfType<KeywordToken>().First();
        public IEnumerable<LiteralToken> ArgLines => Tokens.OfType<LiteralToken>();
        public IEnumerable<CommentTextToken> Comments => Tokens.OfType<CommentTextToken>();
            
        public override LineType Type => LineType.Instruction;

        public static bool IsInstruction(string text, char escapeChar) =>
            DockerfileParser.Instruction(escapeChar).TryParse(text).WasSuccessful;

        public static Instruction Create(string instruction, string args) =>
            Parse($"{instruction} {args}", DefaultEscapeChar);

        public static Instruction Parse(string text, char escapeChar) =>
            new Instruction(text, escapeChar);
    }
}
