using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class DockerfileTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ParseTestScenario<Dockerfile> scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                Dockerfile result = Dockerfile.Parse(scenario.Text);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Lines, scenario.TokenValidators);
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
            var testInputs = new ParseTestScenario<Dockerfile>[]
            {
                new ParseTestScenario<Dockerfile>
                {
                    Text = "FROM scratch",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<FromInstruction>(line, "FROM scratch")
                    }
                },
                new ParseTestScenario<Dockerfile>
                {
                    Text = "FROM \\\r\nscratch",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<FromInstruction>(line, $"FROM \\\r\nscratch")
                    }
                },
                new ParseTestScenario<Dockerfile>
                {
                    Text = $"# escape=`\nFROM `\r\n  scratch",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<ParserDirective>(line, "# escape=`\n"),
                        line => ValidateAggregate<FromInstruction>(line, $"FROM `\r\n  scratch",
                            token => ValidateKeyword(token, "FROM"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLineContinuation(token, $"`"),
                            token => ValidateNewLine(token, "\r\n"),
                            token => ValidateWhitespace(token, "  "),
                            token => ValidateLiteral(token, "scratch"))
                    }
                },
                new ParseTestScenario<Dockerfile>
                {
                    Text = $"#comment\r\nFROM scratch",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<Comment>(line, "#comment\r\n"),
                        line => ValidateAggregate<FromInstruction>(line, "FROM scratch")
                    }
                },
                new ParseTestScenario<Dockerfile>
                {
                    Text = $"\r\nFROM scratch\n# test\nRUN foo=\"bar\"\n",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<Whitespace>(line, "\r\n"),
                        line => ValidateAggregate<FromInstruction>(line, "FROM scratch\n"),
                        line => ValidateAggregate<Comment>(line, "# test\n"),
                        line => ValidateAggregate<Instruction>(line, "RUN foo=\"bar\"\n"),
                    }
                },
                new ParseTestScenario<Dockerfile>
                {
                    Text = $"FROM \\\n#comment\nscratch",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<FromInstruction>(line, "FROM \\\n#comment\nscratch",
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
                new ParseTestScenario<Dockerfile>
                {
                    Text = $"RUN apt-get update \\\r\n  && apt-get install curl\r\n\r\n#testing",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<Instruction>(line, "RUN apt-get update \\\r\n  && apt-get install curl\r\n",
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
                        line => ValidateAggregate<Whitespace>(line, "\r\n"),
                        line => ValidateAggregate<Comment>(line, "#testing")
                    }
                },
                new ParseTestScenario<Dockerfile>
                {
                    Text = $"RUN apk add \\\n  userspace-rcu",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<Instruction>(line, "RUN apk add \\\n  userspace-rcu",
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
                new ParseTestScenario<Dockerfile>
                {
                    Text = $"ENV \\\t \n VAR=VAL",
                    TokenValidators = new Action<Token>[]
                    {
                        line => ValidateAggregate<Instruction>(line, "ENV \\\t \n VAR=VAL",
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
    }
}
