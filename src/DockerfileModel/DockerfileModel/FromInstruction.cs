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
    public class FromInstruction : InstructionBase
    {
        private LiteralToken imageName;

        private FromInstruction(string text, char escapeChar)
            : base(text, GetParser(escapeChar))
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
            get => this.PlatformFlag?.Platform;
            set
            {
                PlatformFlag? platformFlag = PlatformFlag;
                if (platformFlag != null && value is not null)
                {
                    platformFlag.Platform = value;
                }
                else
                {
                    PlatformFlag = String.IsNullOrEmpty(value) ? null : PlatformFlag.Create(value!);
                }
            }
        }

        public PlatformFlag? PlatformFlag
        {
            get => this.Tokens.OfType<PlatformFlag>().FirstOrDefault();
            set
            {
                SetToken(PlatformFlag, value,
                    addToken: token =>
                    {
                        this.TokenList.InsertRange(2, new Token[]
                        {
                            token,
                            Whitespace.Create(" ")
                        });
                    },
                    removeToken: token =>
                        this.TokenList.RemoveRange(this.TokenList.IndexOf(token), 2));
            }
        }

        public string? StageName
        {
            get => this.Tokens.OfType<StageName>().FirstOrDefault()?.Stage;
            set
            {
                StageName? stageName = StageNameToken;
                if (stageName != null && value is not null)
                {
                    stageName.Stage = value;
                }
                else
                {
                    StageNameToken = String.IsNullOrEmpty(value) ? null : DockerfileModel.StageName.Create(value!);
                }
            }
        }

        public StageName? StageNameToken
        {
            get => this.Tokens.OfType<StageName>().FirstOrDefault();
            set
            {
                SetToken(StageNameToken, value,
                    addToken: token =>
                    {
                        this.TokenList.AddRange(new Token[]
                        {
                            Whitespace.Create(" "),
                            token,
                        });
                    },
                    removeToken: token =>
                        this.TokenList.RemoveRange(this.TokenList.IndexOf(token) - 1, 2));
            }
        }

        public static FromInstruction Parse(string text, char escapeChar) =>
            new FromInstruction(text, escapeChar);

        public static FromInstruction Create(string imageName, string? stageName = null, string? platform = null,
            char escapeChar = Dockerfile.DefaultEscapeChar)
        {
            StringBuilder builder = new StringBuilder("FROM ");
            if (platform != null)
            {
                builder.Append($"--platform={platform} ");
            }

            builder.Append(imageName);

            if (stageName != null)
            {
                builder.Append($" AS {stageName}");
            }

            return Parse(builder.ToString(), escapeChar);
        }

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar) =>
            Instruction("FROM", escapeChar, GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            (from platform in GetPlatformParser(escapeChar).Optional()
            from imageName in GetImageNameParser(escapeChar)
            from stageName in GetStageNameParser(escapeChar).Optional()
            select ConcatTokens(
                platform.GetOrDefault(),
                imageName,
                stageName.GetOrDefault())).End();

        private static Parser<IEnumerable<Token>> GetPlatformParser(char escapeChar) =>
            ArgTokens(PlatformFlag.GetParser(escapeChar).AsEnumerable(), escapeChar);

        private static Parser<IEnumerable<Token>> GetStageNameParser(char escapeChar) =>
            ArgTokens(DockerfileModel.StageName.GetParser(escapeChar).AsEnumerable(), escapeChar);

        private static Parser<IEnumerable<Token>> GetImageNameParser(char escapeChar) =>
            ArgTokens(
                LiteralAggregate(escapeChar).AsEnumerable(), escapeChar);
    }
}
