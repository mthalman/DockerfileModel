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
        private MountFlag(string text, char escapeChar)
            : base(text, GetInnerParser(escapeChar))
        {
        }

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

        private KeyValueToken<Mount> MountKeyValueToken => Tokens.OfType<KeyValueToken<Mount>>().First();

        public static MountFlag Create(Mount mount) =>
            Parse($"--mount={mount}", Instruction.DefaultEscapeChar);

        public static MountFlag Parse(string text, char escapeChar) =>
            new MountFlag(text, escapeChar);

        public static Parser<MountFlag> GetParser(char escapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new MountFlag(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Flag(escapeChar, KeyValueToken<Mount>.GetParser("mount", escapeChar, GetMount(escapeChar)));

        private static Parser<Mount> GetMount(char escapeChar) =>
            SecretMount.GetParser(escapeChar);
    }
}
