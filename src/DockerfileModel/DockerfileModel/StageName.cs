using System.Collections.Generic;
using System.Linq;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class StageName : AggregateToken
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

        public IdentifierToken Stage
        {
            get => this.stage;
            set
            {
                Requires.NotNull(value, nameof(value));
                this.stage = value;
                this.TokenList[2] = this.stage;
            }
        }

        public static StageName Create(string stageName, char escapeChar) =>
            Parse($"AS {stageName}", escapeChar);

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
            from stageName in Identifier()
            select new IdentifierToken(stageName);

        private void Initialize()
        {
            this.stage = this.TokenList.OfType<IdentifierToken>().First();
        }
    }
}
