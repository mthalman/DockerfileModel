using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class FromInstruction : Instruction
    {
        private LiteralToken imageName;

        private FromInstruction(IEnumerable<Token> tokens) : base(tokens)
        {
            PlatformFlag? platform = this.PlatformFlag;
            int startIndex = 0;
            if (platform != null)
            {
                startIndex = this.TokenList.IndexOf(platform) + 1;
            }

            this.imageName = this.TokenList
                .Skip(startIndex)
                .OfType<LiteralToken>()
                .First();
        }

        public string ImageName
        {
            get => this.imageName.Value;
            set
            {
                Requires.NotNullOrEmpty(value, nameof(value));
                this.imageName.Value = value;
            }
        }

        public LiteralToken ImageNameToken
        {
            get => this.imageName;
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(ImageNameToken, value);
                this.imageName = value;
            }
        }

        public string? Platform
        {
            get => this.PlatformFlag?.Value;
            set => SetOptionalLiteralTokenValue(PlatformToken, value, token => PlatformToken = token);
        }

        public LiteralToken? PlatformToken
        {
            get => PlatformFlag?.ValueToken;
            set => SetOptionalKeyValueTokenValue(
                PlatformFlag, value, val => PlatformFlag.Create(val), token => PlatformFlag = token);
        }

        private PlatformFlag? PlatformFlag
        {
            get => this.Tokens.OfType<PlatformFlag>().FirstOrDefault();
            set => SetOptionalFlagToken(PlatformFlag, value);
        }

        public string? StageName
        {
            get => StageNameToken?.Value;
            set => SetOptionalTokenValue(StageNameToken, value, val => new IdentifierToken(val), token => StageNameToken = token);
        }

        public IdentifierToken? StageNameToken
        {
            get => this.Tokens.OfType<IdentifierToken>().FirstOrDefault();
            set
            {
                SetToken(StageNameToken, value,
                    addToken: token =>
                    {
                        this.TokenList.AddRange(new Token[]
                        {
                            new WhitespaceToken(" "),
                            new KeywordToken("AS"),
                            new WhitespaceToken(" "),
                            token,
                        });
                    },
                    removeToken: token =>
                    {
                        TokenList.RemoveRange(
                            TokenList.FirstPreviousOfType<Token, WhitespaceToken>(TokenList.FirstPreviousOfType<Token, KeywordToken>(token)),
                            token);
                    });
            }
        }

        public static FromInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new FromInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        public static FromInstruction Create(string imageName, string? stageName = null, string? platform = null,
            char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            Requires.NotNullOrEmpty(imageName, nameof(imageName));

            StringBuilder builder = new StringBuilder("FROM ");
            if (platform is not null)
            {
                builder.Append($"{PlatformFlag.Create(platform)} ");
            }

            builder.Append(imageName);

            if (stageName is not null)
            {
                builder.Append($" AS {stageName}");
            }

            return Parse(builder.ToString(), escapeChar);
        }

        public static Parser<FromInstruction> GetParser(char escapeChar = Dockerfile.DefaultEscapeChar) =>
            from tokens in GetInnerParser(escapeChar)
            select new FromInstruction(tokens);

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            Instruction("FROM", escapeChar, GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            (from platform in GetPlatformParser(escapeChar).Optional()
            from imageName in GetImageNameParser(escapeChar)
            from stageName in GetStageNameParser(escapeChar).Optional()
            select ConcatTokens(
                platform.GetOrDefault(),
                imageName,
                stageName.GetOrDefault())).End();

        private static Parser<IEnumerable<Token>> GetStageNameParser(char escapeChar) =>
           from asKeyword in ArgTokens(Keyword("AS", escapeChar).AsEnumerable(), escapeChar)
           from stageName in ArgTokens(StageNameIdentifier().AsEnumerable(), escapeChar)
           select ConcatTokens(asKeyword, stageName);

        private static Parser<IdentifierToken> StageNameIdentifier() =>
            from stageName in Sprache.Parse.Identifier(
                Sprache.Parse.Letter,
                Sprache.Parse.LetterOrDigit.Or(Sprache.Parse.Char('_')).Or(Sprache.Parse.Char('-')).Or(Sprache.Parse.Char('.')))
            select new IdentifierToken(stageName);

        private static Parser<IEnumerable<Token>> GetPlatformParser(char escapeChar) =>
            ArgTokens(PlatformFlag.GetParser(escapeChar).AsEnumerable(), escapeChar);

        private static Parser<IEnumerable<Token>> GetImageNameParser(char escapeChar) =>
            ArgTokens(LiteralAggregate(escapeChar).AsEnumerable(), escapeChar);
    }
}
