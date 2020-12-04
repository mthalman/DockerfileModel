using System.Collections.Generic;
using DockerfileModel.Tokens;
using Sprache;

namespace DockerfileModel
{
    public class ChangeOwnerFlag : KeyValueToken<KeywordToken, ChangeOwner>
    {
        public ChangeOwnerFlag(ChangeOwner changeOwner, char escapeChar = Dockerfile.DefaultEscapeChar)
            : base(new KeywordToken("chown", escapeChar), changeOwner, isFlag: true)
        {
        }

        internal ChangeOwnerFlag(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public static ChangeOwnerFlag Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse(text,
                KeywordToken.GetParser("chown", escapeChar),
                ChangeOwnerParser(escapeChar),
                tokens => new ChangeOwnerFlag(tokens),
                escapeChar: escapeChar);

        public static Parser<ChangeOwnerFlag> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            GetParser(
                KeywordToken.GetParser("chown", escapeChar),
                ChangeOwnerParser(escapeChar),
                tokens => new ChangeOwnerFlag(tokens),
                escapeChar: escapeChar);

        private static Parser<ChangeOwner> ChangeOwnerParser(char escapeChar) =>
            ChangeOwner.GetParser(escapeChar);
    }
}
