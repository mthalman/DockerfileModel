using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class UserInstruction : Instruction
    {
        public UserInstruction(string maintainer, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(maintainer, escapeChar))
        {
        }

        private UserInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
        }

        public string Maintainer
        {
            get => MaintainerToken.Value;
            set
            {
                Requires.NotNull(value, nameof(value));
                MaintainerToken.Value = value;
            }
        }

        public LiteralToken MaintainerToken
        {
            get => Tokens.OfType<LiteralToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(MaintainerToken, value);
            }
        }
   
        public static UserInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new UserInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<UserInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new UserInstruction(tokens);

        private static IEnumerable<Token> GetTokens(string maintainer, char escapeChar)
        {
            Requires.NotNull(maintainer, nameof(maintainer));
            return GetTokens($"MAINTAINER {(String.IsNullOrEmpty(maintainer) ? "\"\"" : maintainer)}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Instruction("MAINTAINER", escapeChar, GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            ArgTokens(
                LiteralWithVariables(
                    escapeChar, whitespaceMode: WhitespaceMode.Allowed).AsEnumerable(), escapeChar, excludeTrailingWhitespace: true);
    }
}
