using System.Collections.Generic;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;

namespace Valleysoft.DockerfileModel
{
    public class ChangeOwnerFlag : KeyValueToken<KeywordToken, UserAccount>
    {
        public ChangeOwnerFlag(UserAccount changeOwner, char escapeChar = Dockerfile.DefaultEscapeChar)
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

        private static Parser<UserAccount> ChangeOwnerParser(char escapeChar) =>
            UserAccount.GetParser(escapeChar);
    }
}
