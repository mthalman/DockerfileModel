using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Sprache;

namespace DockerfileModel
{
    public abstract class InstructionBase : DockerfileLine
    {
        protected InstructionBase(string text, Parser<IEnumerable<Token?>> parser)
            : base(text, parser)
        {
        }

        public string InstructionName
        {
            get => this.InstructionNameToken.Value;
            set { this.InstructionNameToken.Value = value; }
        }

        private KeywordToken InstructionNameToken => Tokens.OfType<KeywordToken>().First();

        public IEnumerable<CommentTextToken> Comments => GetComments();

        public override LineType Type => LineType.Instruction;

        public void ResolveArgValues(IDictionary<string, string> argValues, char escapeChar)
        {
            new ArgResolver(argValues, escapeChar).Visit(this);
        }

        private class ArgResolver : TokenVisitor
        {
            private const string LeadingGroup = "leading";
            private const string ArgGroup = "arg";
            private const string TrailingGroup = "trailing";
            private readonly IDictionary<string, (Regex Regex, string Value)> argValueReplacements;

            public ArgResolver(IDictionary<string, string> argValues, char escapeChar)
            {
                argValueReplacements = argValues
                    .ToDictionary(kvp => kvp.Key,
                        kvp => (new Regex($@"(?<{LeadingGroup}>^|.*[^\{escapeChar}])(?<{ArgGroup}>\${kvp.Key})(?<{TrailingGroup}>\s|\W|$)"), kvp.Value));
            }

            protected override void VisitLiteralToken(LiteralToken token)
            {
                base.VisitLiteralToken(token);
                ReplaceArgs(token);
            }

            private void ReplaceArgs(Token token)
            {
                foreach (var kvp in argValueReplacements)
                {
                    token.Value = kvp.Value.Regex.Replace(token.Value, $"${{{LeadingGroup}}}{kvp.Value.Value}${{{TrailingGroup}}}");
                }
            }
        }
    }
}
