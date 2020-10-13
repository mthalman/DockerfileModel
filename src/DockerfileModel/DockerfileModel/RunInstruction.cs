using System.Collections.Generic;
using System.Linq;
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
            from tokenSets in ArgTokens(
                LiteralContainer(escapeChar, true, tokens => new RunArgs(tokens)).AsEnumerable(),
                escapeChar)
                .Many()
            select new Token[] {
                new RunArgs(
                    tokenSets
                        .SelectMany(tokenSet =>
                            tokenSet
                                .SelectMany(token => token is RunArgs runArgs ? runArgs.Tokens : new Token[] { token })))
            };
    }

    public class RunArgs : QuotableAggregateToken
    {
        public RunArgs(IEnumerable<Token> tokens) : base(tokens, typeof(LiteralToken))
        {
        }
    }
}
