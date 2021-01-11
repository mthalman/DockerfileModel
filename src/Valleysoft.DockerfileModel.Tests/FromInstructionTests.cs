using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests
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
                scenario.Validate?.Invoke(result);
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
            FromInstruction result = new FromInstruction(scenario.ImageName, scenario.Stage, scenario.Platform);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void ImageName()
        {
            FromInstruction instruction = new FromInstruction("test");
            Assert.Equal("test", instruction.ImageName);
            Assert.Equal("test", instruction.ImageNameToken.Value);

            instruction.ImageName = "test2";
            Assert.Equal("test2", instruction.ImageName);
            Assert.Equal("test2", instruction.ImageNameToken.Value);

            instruction.ImageNameToken.Value = "test3";
            Assert.Equal("test3", instruction.ImageName);
            Assert.Equal("test3", instruction.ImageNameToken.Value);

            instruction.ImageNameToken = new LiteralToken("test4");
            Assert.Equal("test4", instruction.ImageName);
            Assert.Equal("test4", instruction.ImageNameToken.Value);

            Assert.Throws<ArgumentNullException>(() => instruction.ImageName = null);
            Assert.Throws<ArgumentException>(() => instruction.ImageName = "");
            Assert.Throws<ArgumentNullException>(() => instruction.ImageNameToken = null);
        }

        [Fact]
        public void ImageNameWithVariables()
        {
            FromInstruction instruction = new FromInstruction("$var");
            TestHelper.TestVariablesWithLiteral(() => instruction.ImageNameToken, "var", canContainVariables: true);
        }

        [Fact]
        public void Platform()
        {
            FromInstruction instruction = new FromInstruction("test");
            Assert.Null(instruction.Platform);
            Assert.Null(instruction.PlatformToken);

            instruction.Platform = "foo";
            Assert.Equal("foo", instruction.Platform);
            Assert.Equal("foo", instruction.PlatformToken.Value);

            instruction.PlatformToken.Value = "foo2";
            Assert.Equal("foo2", instruction.Platform);
            Assert.Equal("foo2", instruction.PlatformToken.Value);

            instruction.Platform = null;
            Assert.Null(instruction.Platform);
            Assert.Null(instruction.PlatformToken);

            instruction.Platform = "";
            Assert.Null(instruction.Platform);
            Assert.Null(instruction.PlatformToken);

            instruction.PlatformToken = new LiteralToken("foo3");
            Assert.Equal("foo3", instruction.Platform);
            Assert.Equal("foo3", instruction.PlatformToken.Value);

            instruction.PlatformToken = null;
            Assert.Null(instruction.Platform);
            Assert.Null(instruction.PlatformToken);

            instruction = FromInstruction.Parse("FROM `\n`\n  alpine", '`');
            instruction.PlatformToken = new LiteralToken("linux/amd64");
            Assert.Equal("FROM --platform=linux/amd64 `\n`\n  alpine", instruction.ToString());

            instruction = FromInstruction.Parse("FROM `\n`\n --platform=linux/amd64 alpine", '`');
            instruction.PlatformToken = null;
            Assert.Equal("FROM `\n`\n alpine", instruction.ToString());
        }

        [Fact]
        public void PlatformWithVariables()
        {
            FromInstruction instruction = new FromInstruction("scratch", platform: "$var");
            TestHelper.TestVariablesWithNullableLiteral(
                () => instruction.PlatformToken, token => instruction.PlatformToken = token, val => instruction.Platform = val, "var", canContainVariables: true);
        }

        [Fact]
        public void StageName()
        {
            FromInstruction instruction = new FromInstruction("test");
            Assert.Null(instruction.StageName);
            Assert.Null(instruction.StageNameToken);

            instruction.StageName = "foo";
            Assert.Equal("foo", instruction.StageName);
            Assert.Equal("foo", instruction.StageNameToken.Value);

            instruction.StageNameToken.Value = "foo2";
            Assert.Equal("foo2", instruction.StageName);
            Assert.Equal("foo2", instruction.StageNameToken.Value);

            instruction.StageName = null;
            Assert.Null(instruction.StageName);
            Assert.Null(instruction.StageNameToken);

            instruction.StageName = "";
            Assert.Null(instruction.StageName);
            Assert.Null(instruction.StageNameToken);

            instruction.StageNameToken = new StageName("foo3");
            Assert.Equal("foo3", instruction.StageName);
            Assert.Equal("foo3", instruction.StageNameToken.Value);

            instruction.StageNameToken = null;
            Assert.Null(instruction.StageName);
            Assert.Null(instruction.StageNameToken);
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
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
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
                        token => ValidateKeyword(token, "as"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier<StageName>(token, "build")
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
                    Text = "FROM alpine`\n as build",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine"),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyword(token, "as"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier<StageName>(token, "build")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("alpine", result.ImageName);
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
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateLiteral(token, "alpine:latest"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateKeyword(token, "as"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateAggregate<CommentToken>(token, "#comment\n",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateString(token, "comment"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateIdentifier<StageName>(token, "build")
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
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "linux/amd64")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyword(token, "as"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier<StageName>(token, "build")
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
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, '='),
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
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateAggregate<PlatformFlag>(token, "--platform=linux/amd64",
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "linux/amd64")),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
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
                    Text = "FROM al\\\npine",
                    EscapeChar = '\\',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "al\\\npine", null,
                            token => ValidateString(token, "al"),
                            token => ValidateAggregate<LineContinuationToken>(token, "\\\n",
                                token => ValidateSymbol(token, '\\'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "pine"))
                    }
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM alpine AS bui`\nld",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyword(token, "AS"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<StageName>(token, "bui`\nld",
                            token => ValidateString(token, "bui"),
                            token => ValidateLineContinuation(token, '`', "\n"),
                            token => ValidateString(token, "ld"))
                    }
                },
                new FromInstructionParseTestScenario
                {
                    Text = "FROM \"al\\\npine\"",
                    EscapeChar = '\\',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "FROM"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "al\\\npine", '\"',
                            token => ValidateString(token, "al"),
                            token => ValidateAggregate<LineContinuationToken>(token, "\\\n",
                                token => ValidateSymbol(token, '\\'),
                                token => ValidateNewLine(token, "\n")),
                            token => ValidateString(token, "pine"))
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
                    ParseExceptionPosition = new Position(1, 1, 22)
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
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, '='),
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
                        token => ValidateKeyword(token, "AS"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier<StageName>(token, "test")
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
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateKeyword(token, "platform"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "windows/amd64")
                        ),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "alpine:latest"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyword(token, "AS"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier<StageName>(token, "test")
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
