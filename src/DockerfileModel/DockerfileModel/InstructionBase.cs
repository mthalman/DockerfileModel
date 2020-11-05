﻿using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public abstract class InstructionBase : DockerfileConstruct, ICommentable
    {
        protected InstructionBase(string text, Parser<IEnumerable<Token?>> parser)
            : base(text, parser)
        {
        }

        public string InstructionName
        {
            get => this.InstructionNameToken.Value;
            set => this.InstructionNameToken.Value = value;
        }

        public KeywordToken InstructionNameToken
        {
            get => Tokens.OfType<KeywordToken>().First();
            set
            {
                Requires.NotNull(value, nameof(value));
                SetToken(InstructionNameToken, value);
            }
        }

        public IList<string?> Comments => GetComments();

        public IEnumerable<CommentToken> CommentTokens => GetCommentTokens();

        public override ConstructType Type => ConstructType.Instruction;

        protected static Parser<IEnumerable<Token>> InstructionArgs(char escapeChar) =>
            from lineSets in (CommentText().Or(InstructionArgLine(escapeChar))).Many()
            select lineSets.SelectMany(lineSet => lineSet);

        private static Parser<IEnumerable<Token>> InstructionArgLine(char escapeChar) =>
            from text in Parse.AnyChar.Except(LineContinuation(escapeChar)).Except(Parse.LineEnd).Many().Text()
            from lineContinuation in LineContinuation(escapeChar).Optional()
            from lineEnd in OptionalNewLine().AsEnumerable()
            select ConcatTokens(
                GetInstructionArgLineContent(text),
                new Token[] { lineContinuation.GetOrDefault() },
                lineEnd);

        private static IEnumerable<Token?> GetInstructionArgLineContent(string text)
        {
            if (text.Length == 0)
            {
                yield break;
            }

            if (text.Trim().Length == 0)
            {
                yield return new WhitespaceToken(text);
                yield break;
            }

            yield return GetLeadingWhitespaceToken(text);
            yield return new LiteralToken(text.Trim());
            yield return GetTrailingWhitespaceToken(text);
        }

        private static WhitespaceToken? GetLeadingWhitespaceToken(string text)
        {
            string? whitespace = new string(
                text
                    .TakeWhile(ch => Char.IsWhiteSpace(ch))
                    .ToArray());

            if (whitespace == String.Empty)
            {
                return null;
            }

            return new WhitespaceToken(whitespace);
        }
    }
}
