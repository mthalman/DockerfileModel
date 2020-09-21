using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class FromInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(FromInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                FromInstruction result = FromInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => FromInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            FromInstruction result = FromInstruction.Create(scenario.ImageName, scenario.Stage, scenario.Platform);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            FromInstructionParseTestScenario[] testInputs = new FromInstructionParseTestScenario[]
            {
                new FromInstructionParseTestScenario
                {
                    Text = "FROM scratch",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "scratch")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("scratch", result.ImageName);
                        Assert.Equal("FROM", result.InstructionName);
                        Assert.Null(result.Platform);
                        Assert.Null(result.StageName);
                    }
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM `\nscratch",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, "`"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateLiteral(token, "scratch")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("scratch", result.ImageName);
                        Assert.Equal("FROM", result.InstructionName);
                        Assert.Null(result.Platform);
                        Assert.Null(result.StageName);
                    }
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM alpine:latest as build",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine:latest"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<StageName>(token, "as build",
                            token => ValidateKeyword(token, "as"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateIdentifier(token, "build"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("alpine:latest", result.ImageName);
                        Assert.Equal("FROM", result.InstructionName);
                        Assert.Null(result.Platform);
                        Assert.Equal("build", result.StageName);
                    }
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM `\nalpine:latest `\nas `\n#comment\nbuild",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, "`"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateLiteral(token, "alpine:latest"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, "`"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateAggregate<StageName>(token, "as `\n#comment\nbuild",
                            token => ValidateKeyword(token, "as"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLineContinuation(token, "`"),
                            token => ValidateNewLine(token, "\n"),
                            token => ValidateAggregate<CommentToken>(token, "#comment",
                                token => ValidateSymbol(token, "#"),
                                token => ValidateLiteral(token, "comment")),
                            token => ValidateNewLine(token, "\n"),
                            token => ValidateIdentifier(token, "build"))
                    },
                    Validate = result =>
                    {
                        Assert.Collection(result.Comments,
                            comment => Assert.Equal("comment", comment));

                        result.Comments[0] = "new comment";
                        Assert.Collection(result.Comments,
                            comment => Assert.Equal("new comment", comment));
                        Assert.Equal("FROM `\nalpine:latest `\nas `\n#new comment\nbuild", result.ToString());

                        Assert.Equal("alpine:latest", result.ImageName);
                        Assert.Equal("FROM", result.InstructionName);
                        Assert.Null(result.Platform);
                        Assert.Equal("build", result.StageName);
                    }
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM --platform=linux/amd64 alpine as build",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<PlatformFlag>(token, "--platform=linux/amd64",
                            token => ValidateSymbol(token, "--"),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, "="),
                            token => ValidateLiteral(token, "linux/amd64")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<StageName>(token, "as build",
                            token => ValidateKeyword(token, "as"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateIdentifier(token, "build"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("alpine", result.ImageName);
                        Assert.Equal("FROM", result.InstructionName);
                        Assert.Equal("linux/amd64", result.Platform);
                        Assert.Equal("build", result.StageName);
                    }
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM --platform=linux/amd64 alpine",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<PlatformFlag>(token, "--platform=linux/amd64",
                            token => ValidateSymbol(token, "--"),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, "="),
                            token => ValidateLiteral(token, "linux/amd64")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("alpine", result.ImageName);
                        Assert.Equal("FROM", result.InstructionName);
                        Assert.Equal("linux/amd64", result.Platform);
                        Assert.Null(result.StageName);
                    }
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM `\n  --platform=linux/amd64`\n  alpine",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, "`"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateAggregate<PlatformFlag>(token, "--platform=linux/amd64",
                            token => ValidateSymbol(token, "--"),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, "="),
                            token => ValidateLiteral(token, "linux/amd64")),
                        token => ValidateLineContinuation(token, "`"),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateLiteral(token, "alpine")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("alpine", result.ImageName);
                        Assert.Equal("FROM", result.InstructionName);
                        Assert.Equal("linux/amd64", result.Platform);
                        Assert.Null(result.StageName);
                    }
                },
                new FromInstructionParseTestScenario
                {
                    Text = "xFROM ",
                    ParseExceptionPosition = new Position(1, 1, 1)
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM ",
                    ParseExceptionPosition = new Position(1, 1, 6)
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM x y",
                    ParseExceptionPosition = new Position(1, 1, 8)
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM platform= alpine",
                    ParseExceptionPosition = new Position(1, 1, 16)
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM alpine AS",
                    ParseExceptionPosition = new Position(1, 1, 13)
                },
            };

            return testInputs.Select(input => new object[] { input });
        }

        public static IEnumerable<object[]> CreateTestInput()
        {
            CreateTestScenario[] testInputs = new CreateTestScenario[]
            {
                new CreateTestScenario
                {
                    ImageName = "alpine:latest",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine:latest")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("alpine:latest", result.ImageName);
                        Assert.Null(result.Platform);
                        Assert.Null(result.StageName);

                        result.ImageName = "alpine";
                        Assert.Equal("alpine", result.ImageName);
                        Assert.Equal("FROM alpine", result.ToString());

                        result.Platform = "linux/arm64";
                        Assert.Equal("linux/arm64", result.Platform);
                        Assert.Equal("FROM --platform=linux/arm64 alpine", result.ToString());

                        result.Platform = null;
                        Assert.Null(result.Platform);
                        Assert.Equal("FROM alpine", result.ToString());

                        result.StageName = "installer";
                        Assert.Equal("installer", result.StageName);
                        Assert.Equal("FROM alpine AS installer", result.ToString());

                        result.StageName = null;
                        Assert.Null(result.StageName);
                        Assert.Equal("FROM alpine", result.ToString());
                    }
                },
                new CreateTestScenario
                {
                    ImageName = "alpine:latest",
                    Platform = "windows/amd64",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<PlatformFlag>(token, "--platform=windows/amd64",
                            token => ValidateSymbol(token, "--"),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, "="),
                            token => ValidateLiteral(token, "windows/amd64")
                        ),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine:latest")
                    }
                },
                new CreateTestScenario
                {
                    ImageName = "alpine:latest",
                    Stage = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine:latest"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<StageName>(token, "AS test",
                            token => ValidateKeyword(token, "AS"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateIdentifier(token, "test")
                        )
                    }
                },
                new CreateTestScenario
                {
                    ImageName = "alpine:latest",
                    Platform = "windows/amd64",
                    Stage = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<PlatformFlag>(token, "--platform=windows/amd64",
                            token => ValidateSymbol(token, "--"),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, "="),
                            token => ValidateLiteral(token, "windows/amd64")
                        ),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine:latest"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<StageName>(token, "AS test",
                            token => ValidateKeyword(token, "AS"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateIdentifier(token, "test")
                        )
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class FromInstructionParseTestScenario : ParseTestScenario<FromInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<FromInstruction>
        {
            public string Platform { get; set; }
            public string ImageName { get; set; }
            public string Stage { get; set; }
        }
    }
}
