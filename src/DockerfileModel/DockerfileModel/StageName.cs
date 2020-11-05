using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class StageName : AggregateToken, ICommentable
    {
        private IdentifierToken stage;

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        private StageName(string text, char escapeChar)
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            : base(text, GetInnerParser(escapeChar))
        {
            Initialize();
        }

#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        internal StageName(IEnumerable<Token> tokens)
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
            : base(tokens)
        {
            Initialize();
        }

        public string Stage
        {
            get => StageToken.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                this.stage.Value = value;
            }
        }

        public IdentifierToken StageToken
        {
            get => this.stage;
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(StageToken, value);
                this.stage = value;
            }
        }

        public IList<string?> Comments => GetComments();

        public IEnumerable<CommentToken> CommentTokens => GetCommentTokens();

        public static StageName Create(string stageName, char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullOrEmpty(stageName, nameof(stageName));
            return Parse($"AS {stageName}", escapeChar);
        }

        public static StageName Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new StageName(text, escapeChar);

        public static Parser<StageName> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new StageName(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            from asKeyword in ArgTokens(AsKeyword().AsEnumerable(), escapeChar)
            from stageName in ArgTokens(StageNameIdentifier().AsEnumerable(), escapeChar)
            select ConcatTokens(
                asKeyword,
                stageName);

        private static Parser<KeywordToken> AsKeyword() =>
            from asKeyword in Sprache.Parse.IgnoreCase("as").Text()
            select new KeywordToken(asKeyword);

        private static Parser<IdentifierToken> StageNameIdentifier() =>
            from stageName in Sprache.Parse.Identifier(
                Sprache.Parse.Letter,
                Sprache.Parse.LetterOrDigit.Or(Sprache.Parse.Char('_')).Or(Sprache.Parse.Char('-')).Or(Sprache.Parse.Char('.')))
            select new IdentifierToken(stageName);

        private void Initialize()
        {
            this.stage = this.TokenList.OfType<IdentifierToken>().First();
        }
    }
}
