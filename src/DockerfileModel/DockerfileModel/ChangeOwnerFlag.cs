using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ChangeOwnerFlag : AggregateToken
    {
        internal ChangeOwnerFlag(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public string User
        {
            get => ChangeOwnerToken.ValueToken.User;
            set
            {
                Requires.NotNull(value, nameof(value));
                ChangeOwnerToken.ValueToken.User = value;
            }
        }

        public string? Group
        {
            get => ChangeOwnerToken.ValueToken.Group;
            set => ChangeOwnerToken.ValueToken.Group = value;
        }

        public KeyValueToken<ChangeOwner> ChangeOwnerToken
        {
            get => Tokens.OfType<KeyValueToken<ChangeOwner>>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(ChangeOwnerToken, value);
            }
        }

        public static ChangeOwnerFlag Create(string user, string? group = null, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNull(user, nameof(user));
            return Parse($"--chown={ChangeOwner.Create(user, group, escapeChar)}", escapeChar);
        }

        public static ChangeOwnerFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ChangeOwnerFlag(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<ChangeOwnerFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ChangeOwnerFlag(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Flag(escapeChar, KeyValueToken<ChangeOwner>.GetParser("chown", escapeChar, ChangeOwner.GetParser(escapeChar)).AsEnumerable());
    }
}
