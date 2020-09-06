using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.LineValidator;
using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class DockerfileTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                Dockerfile result = Dockerfile.Parse(scenario.Text);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Lines, scenario.LineValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => Dockerfile.Parse(scenario.Text));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            var testInputs = new ParseTestScenario[]
            {
                new ParseTestScenario
                {
                    Text = "FROM scratch",
                    LineValidators = new Action<DockerfileLine>[]
                    {
                        line => ValidateLine<Instruction>(line, "FROM scratch")
                    }
                },
                new ParseTestScenario
                {
                    Text = "FROM \\\r\nscratch",
                    LineValidators = new Action<DockerfileLine>[]
                    {
                        line => ValidateLine<Instruction>(line, $"FROM \\\r\nscratch")
                    }
                },
                new ParseTestScenario
                {
                    Text = $"# escape=`\nFROM `\r\n  scratch",
                    LineValidators = new Action<DockerfileLine>[]
                    {
                        line => ValidateLine<ParserDirective>(line, "# escape=`\n"),
                        line => ValidateLine<Instruction>(line, $"FROM `\r\n  scratch",
                            token => ValidateKeyword(token, "FROM"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLineContinuation(token, $"`"),
                            token => ValidateNewLine(token, "\r\n"),
                            token => ValidateWhitespace(token, "  "),
                            token => ValidateLiteral(token, "scratch"))
                    }
                },
                new ParseTestScenario
                {
                    Text = $"#comment\r\nFROM scratch",
                    LineValidators = new Action<DockerfileLine>[]
                    {
                        line => ValidateLine<Comment>(line, "#comment\r\n"),
                        line => ValidateLine<Instruction>(line, "FROM scratch")
                    }
                },
                new ParseTestScenario
                {
                    Text = $"\r\nFROM scratch\n# test\nRUN foo=\"bar\"\n",
                    LineValidators = new Action<DockerfileLine>[]
                    {
                        line => ValidateLine<Whitespace>(line, "\r\n"),
                        line => ValidateLine<Instruction>(line, "FROM scratch\n"),
                        line => ValidateLine<Comment>(line, "# test\n"),
                        line => ValidateLine<Instruction>(line, "RUN foo=\"bar\"\n"),
                    }
                },
                new ParseTestScenario
                {
                    Text = $"FROM \\\n#comment\nscratch",
                    LineValidators = new Action<DockerfileLine>[]
                    {
                        line => ValidateLine<Instruction>(line, "FROM \\\n#comment\nscratch",
                            token => ValidateKeyword(token, "FROM"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLineContinuation(token, "\\"),
                            token => ValidateNewLine(token, "\n"),
                            token => ValidateComment(token),
                            token => ValidateCommentText(token, "comment"),
                            token => ValidateNewLine(token, "\n"),
                            token => ValidateLiteral(token, "scratch")
                        ),
                    }
                },
                new ParseTestScenario
                {
                    Text = $"RUN apt-get update \\\r\n  && apt-get install curl\r\n\r\n#testing",
                    LineValidators = new Action<DockerfileLine>[]
                    {
                        line => ValidateLine<Instruction>(line, "RUN apt-get update \\\r\n  && apt-get install curl\r\n",
                            new Action<Token>[]
                            {
                                token => ValidateKeyword(token, "RUN"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLiteral(token, "apt-get update"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLineContinuation(token, "\\"),
                                token => ValidateNewLine(token, "\r\n"),
                                token => ValidateWhitespace(token, "  "),
                                token => ValidateLiteral(token, "&& apt-get install curl"),
                                token => ValidateNewLine(token, "\r\n"),
                            }),
                        line => ValidateLine<Whitespace>(line, "\r\n"),
                        line => ValidateLine<Comment>(line, "#testing")
                    }
                },
                new ParseTestScenario
                {
                    Text = $"RUN apk add \\\n  userspace-rcu",
                    LineValidators = new Action<DockerfileLine>[]
                    {
                        line => ValidateLine<Instruction>(line, "RUN apk add \\\n  userspace-rcu",
                            new Action<Token>[]
                            {
                                token => ValidateKeyword(token, "RUN"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLiteral(token, "apk add"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLineContinuation(token, "\\"),
                                token => ValidateNewLine(token, "\n"),
                                token => ValidateWhitespace(token, "  "),
                                token => ValidateLiteral(token, "userspace-rcu"),
                            })
                    }
                },
                new ParseTestScenario
                {
                    Text = $"ENV \\\t \n VAR=VAL",
                    LineValidators = new Action<DockerfileLine>[]
                    {
                        line => ValidateLine<Instruction>(line, "ENV \\\t \n VAR=VAL",
                            new Action<Token>[]
                            {
                                token => ValidateKeyword(token, "ENV"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLineContinuation(token, "\\"),
                                token => ValidateWhitespace(token, "\t "),
                                token => ValidateNewLine(token, "\n"),
                                token => ValidateWhitespace(token, " "),
                                token => ValidateLiteral(token, "VAR=VAL"),
                            })
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public abstract class TestScenario
        {
            public Action<Dockerfile> Validate { get; set; }
            public Action<DockerfileLine>[] LineValidators { get; set; }
        }

        public class ParseTestScenario : TestScenario
        {
            public string Text { get; set; }
            public Position ParseExceptionPosition { get; set; }
        }

        public class CreateTestScenario : TestScenario
        {
            public string Comment { get; set; }
        }
    }
}
