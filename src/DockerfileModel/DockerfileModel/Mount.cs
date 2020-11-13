using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;

namespace DockerfileModel
{
    public abstract class Mount : AggregateToken
    {
        protected Mount(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Type
        {
            get => TypeToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                TypeToken.ValueToken.Value = value;
            }
        }

        public KeyValueToken<LiteralToken> TypeToken
        {
            get => Tokens.OfType<KeyValueToken<LiteralToken>>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(TypeToken, value);
            }
        }
    }
}
