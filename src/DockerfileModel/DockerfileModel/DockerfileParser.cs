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
        private static readonly Dictionary<string, Func<string, char, Instruction>> instructionParsers =
            new Dictionary<string, Func<string, char, Instruction>>
            {
                { "ADD", AddInstruction.Parse },
                { "ARG", ArgInstruction.Parse },
                { "CMD", CommandInstruction.Parse },
                { "COPY", CopyInstruction.Parse },
                { "ENTRYPOINT", EntrypointInstruction.Parse },
                { "EXPOSE", ExposeInstruction.Parse },
                { "ENV", EnvInstruction.Parse },
                { "FROM", FromInstruction.Parse },
                { "HEALTHCHECK", HealthCheckInstruction.Parse },
                { "LABEL", GenericInstruction.Parse },
                { "MAINTAINER", GenericInstruction.Parse },
                { "ONBUILD", GenericInstruction.Parse },
                { "RUN", RunInstruction.Parse },
                { "SHELL", GenericInstruction.Parse },
                { "STOPSIGNAL", GenericInstruction.Parse },
                { "USER", GenericInstruction.Parse },
                { "VOLUME", GenericInstruction.Parse },
                { "WORKDIR", GenericInstruction.Parse },
            };

        public static Dockerfile ParseContent(string text)
        {
            bool parserDirectivesComplete = false;
            char escapeChar = Dockerfile.DefaultEscapeChar;

            List<DockerfileConstruct> dockerfileConstructs = new List<DockerfileConstruct>();

            List<string> constructLines = new List<string>();
            StringBuilder constructBuilder = new StringBuilder();
            StringBuilder lineBuilder = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                lineBuilder.Append(ch);

                if (ch == '\n')
                {
                    string line = lineBuilder.ToString();
                    if (!parserDirectivesComplete)
                    {
                        if (ParserDirective.IsParserDirective(line))
                        {
                            ParserDirective? parserDirective = ParserDirective.Parse(line);
                            dockerfileConstructs.Add(parserDirective);
                            constructLines.Add(line);

                            if (parserDirective.DirectiveName.Equals(
                                ParserDirective.EscapeDirective, StringComparison.OrdinalIgnoreCase))
                            {
                                escapeChar = parserDirective.DirectiveValue[0];
                            }
                            lineBuilder = new StringBuilder();
                            continue;
                        }
                        else
                        {
                            parserDirectivesComplete = true;
                        }
                    }

                    bool inLineContinuation = constructBuilder.Length > 0;

                    constructBuilder.Append(line);
                    if (!EndsInLineContinuation(escapeChar).TryParse(line).WasSuccessful &&
                        !(Comment.IsComment(line) && inLineContinuation))
                    {
                        constructLines.Add(constructBuilder.ToString());
                        constructBuilder = new StringBuilder();
                    }

                    lineBuilder = new StringBuilder();
                }
            }

            string lastConstruct = constructBuilder.ToString() + lineBuilder.ToString();

            if (lastConstruct.Length > 0)
            {
                constructLines.Add(lastConstruct);
            }

            for (int i = dockerfileConstructs.Count; i < constructLines.Count; i++)
            {
                string line = constructLines[i];
                if (Whitespace.IsWhitespace(line))
                {
                    dockerfileConstructs.Add(new Whitespace(line));
                }
                else if (Comment.IsComment(line))
                {
                    dockerfileConstructs.Add(Comment.Parse(line));
                }
                else if (GenericInstruction.IsInstruction(line, escapeChar))
                {
                    dockerfileConstructs.Add(CreateInstruction(line, escapeChar));
                }
                else
                {
                    throw new ParseException($"Unexpected line content: {line}", new Position(1, i, 1));
                }
            }

            return new Dockerfile(dockerfileConstructs);
        }

        private static Instruction CreateInstruction(string text, char escapeChar)
        {
            string instructionName = InstructionName(escapeChar).Parse(text);
            return instructionParsers[instructionName](text, escapeChar);
        }

        private static Parser<string> InstructionName(char escapeChar) =>
            from leading in Whitespace()
            from instruction in InstructionIdentifier(escapeChar)
            select instruction.Value;

        private static Parser<LineContinuationToken> EndsInLineContinuation(char escapeChar) =>
            from text in Parse.AnyChar.Except(LineContinuationToken.GetParser(escapeChar)).Many().Text()
            from lineCont in LineContinuationToken.GetParser(escapeChar)
            select lineCont;

        public static Parser<KeywordToken> InstructionIdentifier(char escapeChar) =>
            instructionParsers.Keys
                .Select(instructionName => KeywordToken.GetParser(instructionName, escapeChar))
                .Aggregate((current, next) => current.Or(next));
    }
}
