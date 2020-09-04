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
        public IEnumerable<LiteralToken> ArgLines => Tokens.GetNonCommentLiterals();
        public IEnumerable<LiteralToken> Comments => Tokens.GetCommentLiterals();
            
        public override LineType Type => LineType.Instruction;

        public static bool IsInstruction(string text, char escapeChar) =>
            DockerfileParser.Instruction(escapeChar).TryParse(text).WasSuccessful;

        public static Instruction Create(string instruction, string args) =>
            CreateFromRawText($"{instruction} {args}", DefaultEscapeChar);

        public static Instruction CreateFromRawText(string text, char escapeChar) =>
            new Instruction(text, escapeChar);
    }
}
