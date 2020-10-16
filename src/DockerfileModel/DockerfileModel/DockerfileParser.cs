using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DockerfileModel.Tokens;
using Sprache;

using static DockerfileModel.ParseHelper;

namespace DockerfileModel
{
    internal static class DockerfileParser
    {
        private static readonly Dictionary<string, Func<string, char, InstructionBase>> instructionParsers =
            new Dictionary<string, Func<string, char, InstructionBase>>
            {
                { "ADD", Instruction.Parse },
                { "ARG", ArgInstruction.Parse },
                { "CMD", Instruction.Parse },
                { "COPY", Instruction.Parse },
                { "ENTRYPOINT", Instruction.Parse },
                { "EXPOSE", Instruction.Parse },
                { "ENV", Instruction.Parse },
                { "FROM", FromInstruction.Parse },
                { "HEALTHCHECK", Instruction.Parse },
                { "LABEL", Instruction.Parse },
                { "MAINTAINER", Instruction.Parse },
                { "ONBUILD", Instruction.Parse },
                { "RUN", RunInstruction.Parse },
                { "SHELL", Instruction.Parse },
                { "STOPSIGNAL", Instruction.Parse },
                { "USER", Instruction.Parse },
                { "VOLUME", Instruction.Parse },
                { "WORKDIR", Instruction.Parse },
            };

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

            List<DockerfileConstruct> dockerfileConstructs = new List<DockerfileConstruct>();
            StringBuilder? instructionContent = null;
            for (int i = 0; i < inputLines.Count; i++)
            {
                string line = inputLines[i];
                if (!parserDirectivesComplete)
                {
                    if (ParserDirective.IsParserDirective(line))
                    {
                        ParserDirective? parserDirective = ParserDirective.Parse(line);
                        dockerfileConstructs.Add(parserDirective);

                        if (parserDirective.DirectiveName.Equals(
                            ParserDirective.EscapeDirective, StringComparison.OrdinalIgnoreCase))
                        {
                            escapeChar = parserDirective.DirectiveValue[0];
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
                    dockerfileConstructs.Add(Whitespace.Create(line));
                }
                else if (Comment.IsComment(line))
                {
                    if (instructionContent is null)
                    {
                        dockerfileConstructs.Add(Comment.Parse(line));
                    }
                    else
                    {
                        instructionContent.Append(line);
                    }
                }
                else if (instructionContent is null && Instruction.IsInstruction(line, escapeChar))
                {
                    if (EndsInLineContinuation(escapeChar).TryParse(line).WasSuccessful)
                    {
                        instructionContent = new StringBuilder(line);
                    }
                    else
                    {
                        dockerfileConstructs.Add(CreateInstruction(line, escapeChar));
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
                        dockerfileConstructs.Add(CreateInstruction(instructionContent.ToString(), escapeChar));
                        instructionContent = null;
                    }
                }
            }

            return new Dockerfile(dockerfileConstructs);
        }

        private static InstructionBase CreateInstruction(string text, char escapeChar)
        {
            string instructionName = InstructionName().Parse(text);
            return instructionParsers[instructionName](text, escapeChar);
        }

        private static Parser<string> InstructionName() =>
            from leading in Whitespace()
            from instruction in InstructionIdentifier()
            select instruction.Value;

        private static Parser<LineContinuationToken> EndsInLineContinuation(char escapeChar) =>
            from text in Parse.AnyChar.Except(LineContinuation(escapeChar)).Many().Text()
            from lineCont in LineContinuation(escapeChar)
            select lineCont;

        public static Parser<KeywordToken> InstructionIdentifier()
        {
            Parser<KeywordToken>? parser = null;
            foreach (string instructionName in instructionParsers.Keys)
            {
                if (parser is null)
                {
                    parser = ParseHelper.InstructionIdentifier(instructionName);
                }
                else
                {
                    parser = parser.Or(ParseHelper.InstructionIdentifier(instructionName));
                }
            }

            return parser!;
        }
    }
}
