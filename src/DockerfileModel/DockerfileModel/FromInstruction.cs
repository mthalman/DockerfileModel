﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class FromInstruction : InstructionBase
    {
        private readonly char escapeChar;
        private readonly LiteralToken imageName;

        private FromInstruction(string text, char escapeChar)
            : base(text, GetParser(escapeChar))
        {
            var platform = this.PlatformToken;
            int startIndex = 0;
            if (platform != null)
            {
                startIndex = this.TokenList.IndexOf(platform) + 1;
            }

            this.imageName = this.TokenList
                .Skip(startIndex)
                .OfType<LiteralToken>()
                .First();
            this.escapeChar = escapeChar;
        }

        public string ImageName
        {
            get => this.imageName.Value;
            set { this.imageName.Value = value; }
        }

        public string? Platform
        {
            get => this.PlatformToken?.Value;
            set
            {
                PlatformFlag? platformFlag = this.PlatformFlag;
                if (platformFlag != null)
                {
                    if (value is null)
                    {
                        this.TokenList.RemoveRange(this.TokenList.IndexOf(platformFlag), 2);
                    }
                    else
                    {
                        platformFlag.Platform.Value = value;
                    }
                }
                else if (value != null)
                {
                    this.TokenList.InsertRange(2, new Token[]
                    {
                        PlatformFlag.Create(value),
                        Whitespace.Create(" ")
                    });
                }
            }
        }

        private LiteralToken? PlatformToken => this.PlatformFlag?.Platform;
        private PlatformFlag? PlatformFlag => this.Tokens.OfType<PlatformFlag>().FirstOrDefault();

        public string? StageName
        {
            get => this.Tokens.OfType<StageName>().FirstOrDefault()?.Stage?.Value;
            set
            {
                StageName stageName = this.Tokens.OfType<StageName>().FirstOrDefault();
                if (stageName != null)
                {
                    if (value is null)
                    {
                        this.TokenList.RemoveRange(this.TokenList.IndexOf(stageName) - 1, 2);
                    }
                    else
                    {
                        stageName.Stage.Value = value;
                    }
                }
                else if (value != null)
                {
                    this.TokenList.AddRange(new Token[]
                    {
                        Whitespace.Create(" "),
                        DockerfileModel.StageName.Create(value, escapeChar),
                    });
                }
            }
        }

        public static FromInstruction Parse(string text, char escapeChar) =>
            new FromInstruction(text, escapeChar);

        public static FromInstruction Create(string imageName, string? stageName = null, string? platform = null)
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

            return Parse(builder.ToString(), Instruction.DefaultEscapeChar);
        }

        public static Parser<IEnumerable<Token>> GetParser(char escapeChar) =>
            Instruction("FROM", escapeChar, GetArgsParser(escapeChar));

        private static Parser<IEnumerable<Token>> GetArgsParser(char escapeChar) =>
            (from platform in GetPlatformParser(escapeChar).Optional()
            from imageName in GetImageNameParser(escapeChar)
            from stageName in GetStageNameParser(escapeChar).AsEnumerable().Optional()
            select ConcatTokens(
                platform.GetOrDefault(),
                imageName,
                stageName.GetOrDefault())).End();

        private static Parser<IEnumerable<Token>> GetPlatformParser(char escapeChar) =>
            ArgTokens((
                from tokens in PlatformFlag.GetParser(escapeChar)
                select new PlatformFlag(tokens)).AsEnumerable(), escapeChar);

        private static Parser<StageName> GetStageNameParser(char escapeChar) =>
            from tokens in DockerfileModel.StageName.GetParser(escapeChar)
            select new StageName(tokens);

        private static Parser<IEnumerable<Token>> GetImageNameParser(char escapeChar) =>
            ArgTokens((
                from text in NonCommentToken(escapeChar)
                select new LiteralToken(text)).AsEnumerable(), escapeChar);
    }
}