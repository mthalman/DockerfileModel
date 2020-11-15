using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class MountFlag : AggregateToken
    {
        internal MountFlag(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public Mount Mount
        {
            get => MountKeyValueToken.ValueToken;
            set
            {
                Requires.NotNull(value, nameof(value));
                MountKeyValueToken.ValueToken = value;
            }
        }

        private KeyValueToken<KeywordToken, Mount> MountKeyValueToken => Tokens.OfType<KeyValueToken<KeywordToken, Mount>>().First();

        public static MountFlag Create(Mount mount, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNull(mount, nameof(mount));
            return Parse($"--mount={mount}", escapeChar);
        }

        public static MountFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new MountFlag(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<MountFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new MountFlag(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Flag(escapeChar,
                KeyValueToken<KeywordToken, Mount>.GetParser(
                    Keyword("mount", escapeChar), GetMount(escapeChar), escapeChar: escapeChar).AsEnumerable());

        private static Parser<Mount> GetMount(char escapeChar) =>
            SecretMount.GetParser(escapeChar);
    }
}
