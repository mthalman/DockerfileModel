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
                else
                {
                    dockerfileConstructs.Add(Instruction.CreateInstruction(line, escapeChar));
                }
            }

            return new Dockerfile(dockerfileConstructs);
        }

        private static Parser<LineContinuationToken> EndsInLineContinuation(char escapeChar) =>
            from text in Parse.AnyChar.Except(LineContinuationToken.GetParser(escapeChar)).Many().Text()
            from lineCont in LineContinuationToken.GetParser(escapeChar)
            select lineCont;
    }
}
