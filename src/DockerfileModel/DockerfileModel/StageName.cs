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
            : base(text, GetParser(escapeChar))
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
            get => this.stage.Value;
            set
            {
                Requires.NotNull(value, nameof(value));
                this.stage.Value = value;
                this.TokenList[2] = this.stage;
            }
        }

        public IList<string> Comments => GetComments();

        public IEnumerable<CommentToken> CommentTokens => GetCommentTokens();

        public static StageName Create(string stageName) =>
            Parse($"AS {stageName}", Instruction.DefaultEscapeChar);

        public static StageName Parse(string text, char escapeChar) =>
            new StageName(text, escapeChar);

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar) =>
            ArgTokens(
                from asKeyword in ArgTokens(AsKeyword().AsEnumerable(), escapeChar)
                from stageName in ArgTokens(StageNameIdentifier().AsEnumerable(), escapeChar)
                select ConcatTokens(
                    asKeyword,
                    stageName), escapeChar);

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
