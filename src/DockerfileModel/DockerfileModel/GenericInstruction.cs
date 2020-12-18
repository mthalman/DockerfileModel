using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Validation;
using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    public class GenericInstruction : Instruction
    {
        public GenericInstruction(string instruction, string args, char escapeChar = Dockerfile.DefaultEscapeChar)
            : this(GetTokens(instruction, args, escapeChar))
        {
            
        }

        private GenericInstruction(IEnumerable<Token> tokens)
            : base(tokens)
        {
            ArgLines = new ProjectedItemList<LiteralToken, string>(
                Tokens.OfType<LiteralToken>(),
                token => token.Value,
                (token, value) => token.Value = value);
        }

        public IList<string> ArgLines { get; }

        public static GenericInstruction Parse(string text, char escapeChar = Dockerfile.DefaultEscapeChar) =>
            new GenericInstruction(GetTokens(text, GetInnerParser(escapeChar)));

        private static IEnumerable<Token> GetTokens(string instruction, string args, char escapeChar)
        {
            Requires.NotNullOrEmpty(instruction, nameof(instruction));
            Requires.NotNullOrEmpty(args, nameof(args));
            return GetTokens($"{instruction} {args}", GetInnerParser(escapeChar));
        }

        private static Parser<IEnumerable<Token>> GetInnerParser(char escapeChar) =>
            from leading in Whitespace()
            from instruction in TokenWithTrailingWhitespace(InstructionIdentifier(escapeChar))
            from lineContinuation in LineContinuations(escapeChar).Optional()
            from instructionArgs in InstructionArgs(escapeChar)
            select ConcatTokens(leading, instruction, lineContinuation.GetOrDefault(), instructionArgs);

        protected static Parser<IEnumerable<Token>> InstructionArgs(char escapeChar) =>
            from lineSets in (CommentText().Or(InstructionArgLine(escapeChar))).Many()
            select lineSets.SelectMany(lineSet => lineSet);

        private static Parser<IEnumerable<Token>> InstructionArgLine(char escapeChar) =>
            from text in Sprache.Parse.AnyChar.Except(LineContinuationToken.GetParser(escapeChar)).Except(Sprache.Parse.LineEnd).Many().Text()
            from lineContinuation in LineContinuations(escapeChar).Optional()
            from lineEnd in OptionalNewLine().AsEnumerable()
            select ConcatTokens(
                GetInstructionArgLineContent(text, escapeChar),
                lineContinuation.GetOrDefault(),
                lineEnd);

        private static IEnumerable<Token?> GetInstructionArgLineContent(string text, char escapeChar)
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
            yield return new LiteralToken(text.Trim(), canContainVariables: false, escapeChar);
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
