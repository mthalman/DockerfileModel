using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel.Tokens
{
    public class VariableRefToken : AggregateToken
    {
        public VariableRefToken(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public VariableRefToken(string text, Parser<IEnumerable<Token?>> parser) : base(text, parser)
        {
        }

        public VariableRefToken(string text, Parser<Token> parser) : base(text, parser)
        {
        }

        public bool WrappedInBraces { get; internal set; }
        public string? Modifier { get; set; }
        public string? ModifierValue { get; set; }

        public override string ToString()
        {
            if (WrappedInBraces)
            {
                return $"${{{base.ToString()}}}";
            }

            return $"${base.ToString()}";
        }

        public override string? ResolveVariables(IDictionary<string, string?>? argValues = null, bool updateInline = false)
        {
            string variableRefName = Tokens.First().ToString();
            string? value = null;
            argValues?.TryGetValue(variableRefName, out value);
            return value;
        }
    }
}
