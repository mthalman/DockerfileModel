using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class RunInstruction : InstructionBase
    {
        private RunInstruction(string text, char escapeChar)
            : base(text, GetParser(escapeChar))
        {
        }

        public static RunInstruction Parse(string text, char escapeChar) =>
            new RunInstruction(text, escapeChar);

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar) =>
            Instruction("RUN", escapeChar, GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            ArgTokens(LiteralToken(escapeChar).AsEnumerable(), escapeChar);

        public override string? ResolveVariables(char escapeChar, IDictionary<string, string?>? variables = null, ResolutionOptions? options = null)
        {
            // Do not resolve variables for the command of a RUN instruction. It is shell/runtime-specific.
            return ToString();
        }
    }
}
