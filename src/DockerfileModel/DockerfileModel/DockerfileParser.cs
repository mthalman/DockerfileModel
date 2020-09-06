using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sprache;

namespace DockerfileModel
{
    internal static class DockerfileParser
    {
        public static Dockerfile ParseContent(string text)
        {
            bool parserDirectivesComplete = false;
            char escapeChar = '\\';

            List<string> inputLines = new List<string>();
            StringBuilder lineBuilder = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                lineBuilder.Append(ch);

                if (ch == '\n')
                {
                    inputLines.Add(lineBuilder.ToString());
                    lineBuilder = new StringBuilder();
                }
            }

            if (lineBuilder.Length > 0)
            {
                inputLines.Add(lineBuilder.ToString());
            }

            List<DockerfileLine> dockerfileLines = new List<DockerfileLine>();
            StringBuilder? instructionContent = null;
            for (int i = 0; i < inputLines.Count; i++)
            {
                string line = inputLines[i];
                if (!parserDirectivesComplete)
                {
                    if (ParserDirective.IsParserDirective(line))
                    {
                        var parserDirective = ParserDirective.Parse(line);
                        dockerfileLines.Add(parserDirective);

                        if (parserDirective.Directive.Value.Equals(
                            ParserDirective.EscapeDirective, StringComparison.OrdinalIgnoreCase))
                        {
                            escapeChar = parserDirective.Value.Value[0];
                        }
                        continue;
                    }
                    else
                    {
                        parserDirectivesComplete = true;
                    }
                }

                if (DockerfileModel.Whitespace.IsWhitespace(line))
                {
                    dockerfileLines.Add(DockerfileModel.Whitespace.Create(line));
                }
                else if (Comment.IsComment(line))
                {
                    if (instructionContent is null)
                    {
                        dockerfileLines.Add(Comment.Parse(line));
                    }
                    else
                    {
                        instructionContent.Append(line);
                    }
                }
                else if (instructionContent is null && DockerfileModel.Instruction.IsInstruction(line, escapeChar))
                {
                    if (EndsInLineContinuation(escapeChar).TryParse(line).WasSuccessful)
                    {
                        instructionContent = new StringBuilder(line);
                    }
                    else
                    {
                        dockerfileLines.Add(DockerfileModel.Instruction.Parse(line, escapeChar));
                    }    
                }
                else
                {
                    if (instructionContent is null)
                    {
                        throw new ParseException($"Unexpected line content: {line}", new Position(1, i, 1));
                    }

                    instructionContent.Append(line);

                    if (!EndsInLineContinuation(escapeChar).TryParse(line).WasSuccessful)
                    {
                        dockerfileLines.Add(DockerfileModel.Instruction.Parse(instructionContent.ToString(), escapeChar));
                        instructionContent = null;
                    }
                }
            }

            return new Dockerfile(dockerfileLines);
        }

        public static IEnumerable<T> FilterNulls<T>(IEnumerable<T?>? items) where T : class
        {
            if (items is null)
            {
                yield break;
            }

            foreach (T? item in items.Where(item => item != null))
            {
                yield return item!;
            }
        }

        public static Parser<IEnumerable<Token>> Instruction(char escapeChar) =>
            from leading in WhitespaceChars.AsEnumerable()
            from instruction in TokenWithTrailingWhitespace(InstructionIdentifier())
            from lineContinuation in LineContinuation(escapeChar).Optional()
            from instructionArgs in InstructionArgs(escapeChar)
            select ConcatTokens(leading, instruction, lineContinuation.GetOrDefault(), instructionArgs);

        public static Parser<IEnumerable<Token>> ParserDirectiveParser() =>
            from leading in WhitespaceChars.AsEnumerable()
            from commentChar in TokenWithTrailingWhitespace(CommentChar)
            from directive in TokenWithTrailingWhitespace(DirectiveName)
            from op in TokenWithTrailingWhitespace(OperatorChar)
            from value in TokenWithTrailingWhitespace(DirectiveValue)
            select ConcatTokens(
                leading,
                commentChar,
                directive,
                op,
                value);

        public static Parser<IEnumerable<Token>> Whitespace() =>
            from whitespace in Parse.WhiteSpace.Except(Parse.LineTerminator).XMany().Text()
            from newLine in OptionalNewLine().End()
            select ConcatTokens(
                whitespace.Length > 0 ? new WhitespaceToken(whitespace) : null,
                newLine);

        public static Parser<IEnumerable<Token>> CommentText() =>
            from leading in WhitespaceChars.AsEnumerable()
            from comment in TokenWithTrailingWhitespace(CommentChar)
            from text in TokenWithTrailingWhitespace(str => new CommentTextToken(str))
            from lineEnd in OptionalNewLine().AsEnumerable()
            select ConcatTokens(leading, comment, text, lineEnd);

        private readonly static Parser<WhitespaceToken> WhitespaceChars =
            from whitespace in Parse.WhiteSpace.Many().Text()
            select whitespace != "" ? new WhitespaceToken(whitespace) : null;

        private readonly static Parser<OperatorToken> OperatorChar =
            from op in Parse.String("=").Text()
            select new OperatorToken(op);

        private static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Parser<Token> parser) =>
            from token in parser
            from trailingWhitespace in WhitespaceChars
            select ConcatTokens(token, trailingWhitespace);

        private readonly static Parser<CommentToken> CommentChar =
            from comment in Parse.String("#").Text()
            select new CommentToken(comment);

        private static Parser<IEnumerable<Token>> TokenWithTrailingWhitespace(Func<string, Token> createToken) =>
            from val in Parse.AnyChar.Except(Parse.LineEnd).Many().Text()
            select ConcatTokens(createToken(val.Trim()), GetTrailingWhitespaceToken(val)!);

        private static Parser<IEnumerable<Token>> EndsInLineContinuation(char escapeChar) =>
            from text in Parse.AnyChar.Except(LineContinuation(escapeChar)).Many().Text()
            from lineCont in LineContinuation(escapeChar)
            select lineCont;

        private static Parser<IEnumerable<Token?>> LineContinuation(char escapeChar) =>
            from lineCont in Parse.Char(escapeChar)
            from whitespace in Parse.WhiteSpace.Except(Parse.LineEnd).Many()
            from lineEnding in Parse.LineEnd
            select ConcatTokens(
                new LineContinuationToken(lineCont.ToString()),
                whitespace.Any() ? new WhitespaceToken(new string(whitespace.ToArray())) : null,
                new NewLineToken(lineEnding));

        private static Parser<NewLineToken> OptionalNewLine() =>
            from lineEnd in Parse.LineEnd.Optional()
            select lineEnd.IsDefined ? new NewLineToken(lineEnd.Get()) : null;

        private static Parser<IEnumerable<Token?>> InstructionArgLine(char escapeChar) =>
            from text in Parse.AnyChar.Except(LineContinuation(escapeChar)).Except(Parse.LineEnd).Many().Text()
            from lineContinuation in LineContinuation(escapeChar).Optional()
            from lineEnd in OptionalNewLine().AsEnumerable()
            select ConcatTokens(
                GetInstructionArgLineContent(text),
                lineContinuation.GetOrDefault(),
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

        private static Parser<IEnumerable<Token>> InstructionArgs(char escapeChar) =>
            from lineSets in (CommentText().Or(InstructionArgLine(escapeChar))).Many()
            select lineSets.SelectMany(lineSet => lineSet);


        private static WhitespaceToken? GetLeadingWhitespaceToken(string text)
        {
            var whitespace = new string(
                text
                    .TakeWhile(ch => Char.IsWhiteSpace(ch))
                    .ToArray());

            if (whitespace == String.Empty)
            {
                return null;
            }

            return new WhitespaceToken(whitespace);
        }

        private static WhitespaceToken? GetTrailingWhitespaceToken(string text)
        {
            var whitespace = new string(
                text
                    .Reverse()
                    .TakeWhile(ch => Char.IsWhiteSpace(ch))
                    .Reverse()
                    .ToArray());

            if (whitespace == String.Empty)
            {
                return null;
            }

            return new WhitespaceToken(whitespace);
        }

        private static IEnumerable<Token?> ConcatTokens(params Token?[]? tokens) =>
            FilterNulls(tokens).ToList();

        private static IEnumerable<Token?> ConcatTokens(params IEnumerable<Token?>[] tokens) =>
            ConcatTokens(
                FilterNulls(tokens)
                    .SelectMany(tokens => tokens)
                    .ToArray());
        
        private readonly static Parser<KeywordToken> DirectiveName =
            from name in Identifier()
            select new KeywordToken(name);

        private readonly static Parser<LiteralToken> DirectiveValue =
            from val in Parse.AnyChar.Except(Parse.WhiteSpace).Many().Text()
            select new LiteralToken(val);

        private static Parser<string> Identifier() =>
            Parse.Identifier(Parse.Letter, Parse.LetterOrDigit);

        private static Parser<string> InstructionIdentifier(string instructionName) =>
            Parse.IgnoreCase(instructionName).Text();

        private static Parser<KeywordToken> InstructionIdentifier() =>
            from identifier in
                InstructionIdentifier("ADD")
                .Or(InstructionIdentifier("ARG"))
                .Or(InstructionIdentifier("CMD"))
                .Or(InstructionIdentifier("COPY"))
                .Or(InstructionIdentifier("ENTRYPOINT"))
                .Or(InstructionIdentifier("EXPOSE"))
                .Or(InstructionIdentifier("ENV"))
                .Or(InstructionIdentifier("FROM"))
                .Or(InstructionIdentifier("LABEL"))
                .Or(InstructionIdentifier("RUN"))
                .Or(InstructionIdentifier("SHELL"))
                .Or(InstructionIdentifier("USER"))
                .Or(InstructionIdentifier("WORKDIR"))
            select new KeywordToken(identifier);

        private static string Concat(params string[] strings) =>
            String.Join("", strings);
    }
}
