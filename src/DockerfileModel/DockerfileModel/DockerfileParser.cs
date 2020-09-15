using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    internal static class DockerfileParser
    {
        public static Dockerfile ParseContent(string text)
        {
            bool parserDirectivesComplete = false;
            char escapeChar = DockerfileModel.Instruction.DefaultEscapeChar;

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

                        if (parserDirective.DirectiveName.Value.Equals(
                            ParserDirective.EscapeDirective, StringComparison.OrdinalIgnoreCase))
                        {
                            escapeChar = parserDirective.DirectiveValue.Value[0];
                        }
                        continue;
                    }
                    else
                    {
                        parserDirectivesComplete = true;
                    }
                }

                if (Whitespace.IsWhitespace(line))
                {
                    dockerfileLines.Add(Whitespace.Create(line));
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

        public static Parser<IEnumerable<Token>> Instruction(char escapeChar) =>
            from leading in WhitespaceChars().AsEnumerable()
            from instruction in TokenWithTrailingWhitespace(InstructionIdentifier())
            from lineContinuation in LineContinuation(escapeChar).Optional()
            from instructionArgs in InstructionArgs(escapeChar)
            select ConcatTokens(leading, instruction, lineContinuation.GetOrDefault(), instructionArgs);

        public static Parser<IEnumerable<Token>> ParserDirectiveParser() =>
            from leading in WhitespaceChars().AsEnumerable()
            from commentChar in TokenWithTrailingWhitespace(CommentChar())
            from directive in TokenWithTrailingWhitespace(DirectiveName)
            from op in TokenWithTrailingWhitespace(OperatorChar)
            from value in TokenWithTrailingWhitespace(DirectiveValue)
            select ConcatTokens(
                leading,
                commentChar,
                directive,
                op,
                value);

        private static Parser<IEnumerable<Token>> InstructionArgLine(char escapeChar) =>
            from text in Parse.AnyChar.Except(LineContinuation(escapeChar)).Except(Parse.LineEnd).Many().Text()
            from lineContinuation in LineContinuation(escapeChar).Optional()
            from lineEnd in OptionalNewLine().AsEnumerable()
            select ConcatTokens(
                GetInstructionArgLineContent(text),
                lineContinuation.GetOrDefault(),
                lineEnd);

        private readonly static Parser<OperatorToken> OperatorChar =
            from op in Parse.String("=").Text()
            select new OperatorToken(op);

        private static Parser<IEnumerable<Token>> EndsInLineContinuation(char escapeChar) =>
            from text in Parse.AnyChar.Except(LineContinuation(escapeChar)).Many().Text()
            from lineCont in LineContinuation(escapeChar)
            select lineCont;

        private static Parser<IEnumerable<Token>> InstructionArgs(char escapeChar) =>
            from lineSets in (CommentText().Or(InstructionArgLine(escapeChar))).Many()
            select lineSets.SelectMany(lineSet => lineSet);
        
        private readonly static Parser<KeywordToken> DirectiveName =
            from name in Identifier()
            select new KeywordToken(name);

        private readonly static Parser<LiteralToken> DirectiveValue =
            from val in Parse.AnyChar.Except(Parse.WhiteSpace).Many().Text()
            select new LiteralToken(val);

        private static Parser<KeywordToken> InstructionIdentifier() =>
            ParseHelper.InstructionIdentifier("ADD")
                .Or(ParseHelper.InstructionIdentifier("ARG"))
                .Or(ParseHelper.InstructionIdentifier("CMD"))
                .Or(ParseHelper.InstructionIdentifier("COPY"))
                .Or(ParseHelper.InstructionIdentifier("ENTRYPOINT"))
                .Or(ParseHelper.InstructionIdentifier("EXPOSE"))
                .Or(ParseHelper.InstructionIdentifier("ENV"))
                .Or(ParseHelper.InstructionIdentifier("FROM"))
                .Or(ParseHelper.InstructionIdentifier("HEALTHCHECK"))
                .Or(ParseHelper.InstructionIdentifier("LABEL"))
                .Or(ParseHelper.InstructionIdentifier("MAINTAINER"))
                .Or(ParseHelper.InstructionIdentifier("ONBUILD"))
                .Or(ParseHelper.InstructionIdentifier("RUN"))
                .Or(ParseHelper.InstructionIdentifier("SHELL"))
                .Or(ParseHelper.InstructionIdentifier("STOPSIGNAL"))
                .Or(ParseHelper.InstructionIdentifier("USER"))
                .Or(ParseHelper.InstructionIdentifier("VOLUME"))
                .Or(ParseHelper.InstructionIdentifier("WORKDIR"));

        private static string Concat(params string[] strings) =>
            String.Join("", strings);
    }
}
