using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class ChangeOwner : AggregateToken
    {
        internal ChangeOwner(IEnumerable<Token> tokens)
            : base(tokens)
        {
        }

        public string User
        {
            get => UserToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                UserToken.Value = value;
            }
        }

        public LiteralToken UserToken
        {
            get => Tokens.OfType<LiteralToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(UserToken, value);
            }
        }

        public string? Group
        {
            get => GroupToken?.Value;
            set
            {
                LiteralToken? groupToken = GroupToken;
                if (groupToken is not null && value is not null)
                {
                    groupToken.Value = value;
                }
                else
                {
                    GroupToken = String.IsNullOrEmpty(value) ? null : new LiteralToken(value!);
                }
            }
        }

        public LiteralToken? GroupToken
        {
            get => Tokens.OfType<LiteralToken>().Skip(1).FirstOrDefault();
            set
            {
                SetToken(GroupToken, value,
                    addToken: token =>
                    {
                        TokenList.Add(new SymbolToken(':'));
                        TokenList.Add(token);
                    },
                    removeToken: token =>
                    {
                        TokenList.RemoveRange(
                            TokenList.FirstPreviousOfType<Token, SymbolToken>(token),
                            token);
                    });
            }
        }

        public static ChangeOwner Create(string user, string? group = null, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            Parse($"{user}{(String.IsNullOrEmpty(group) ? "" : $":{group}")}", escapeChar);

        public static ChangeOwner Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new ChangeOwner(GetTokens(text, GetInnerParser(escapeChar)));

        public static Parser<ChangeOwner> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new ChangeOwner(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            UserAndGroup(escapeChar).Or(ArgTokens(UserParser(escapeChar), escapeChar, excludeTrailingWhitespace: true));

        private static Parser<IEnumerable<Token>> UserAndGroup(char escapeChar) =>
            from user in ArgTokens(UserParser(escapeChar), escapeChar)
            from groupSegment in GroupSegment(escapeChar)
            select ConcatTokens(user, groupSegment);

        private static Parser<IEnumerable<Token>> UserParser(char escapeChar) =>
            LiteralAggregate(escapeChar, new char[] { ':' }).AsEnumerable();

        private static Parser<IEnumerable<Token>> GroupSegment(char escapeChar) =>
            from colon in ArgTokens(Symbol(':').AsEnumerable(), escapeChar)
            from @group in ArgTokens(
                LiteralAggregate(escapeChar, Enumerable.Empty<char>()).AsEnumerable(), escapeChar, excludeTrailingWhitespace: true)
            select ConcatTokens(colon, @group);
    }
}
