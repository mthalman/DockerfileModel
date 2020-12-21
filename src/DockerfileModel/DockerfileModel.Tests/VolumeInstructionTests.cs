using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class VolumeInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(VolumeInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                VolumeInstruction result = VolumeInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => VolumeInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            VolumeInstruction result = new VolumeInstruction(scenario.Paths);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Paths()
        {
            VolumeInstruction result = new VolumeInstruction("/var/db");
            Assert.Collection(result.Paths, new Action<string>[]
            {
                path => Assert.Equal("/var/db", path)
            });
            Assert.Equal("VOLUME [\"/var/db\"]", result.ToString());
            
            result.Paths[0] = "/var/db1";
            Assert.Collection(result.Paths, new Action<string>[]
            {
                path => Assert.Equal("/var/db1", path)
            });
            Assert.Equal("VOLUME [\"/var/db1\"]", result.ToString());

            result.PathTokens[0].Value = "/var/db2";
            Assert.Collection(result.Paths, new Action<string>[]
            {
                path => Assert.Equal("/var/db2", path)
            });
            Assert.Equal("VOLUME [\"/var/db2\"]", result.ToString());

            result = new VolumeInstruction(new string[] { "/var/db3", "/var/db4" });
            Assert.Collection(result.Paths, new Action<string>[]
            {
                path => Assert.Equal("/var/db3", path),
                path => Assert.Equal("/var/db4", path)
            });
            Assert.Equal("VOLUME [\"/var/db3\", \"/var/db4\"]", result.ToString());

            result.Paths[1] = "/var/db5";
            Assert.Collection(result.Paths, new Action<string>[]
            {
                path => Assert.Equal("/var/db3", path),
                path => Assert.Equal("/var/db5", path)
            });
            Assert.Equal("VOLUME [\"/var/db3\", \"/var/db5\"]", result.ToString());
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            VolumeInstructionParseTestScenario[] testInputs = new VolumeInstructionParseTestScenario[]
            {
                new VolumeInstructionParseTestScenario
                {
                    Text = "VOLUME /var/log",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "VOLUME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "/var/log")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("VOLUME", result.InstructionName);
                        Assert.Collection(result.Paths, new Action<string>[]
                        {
                            path => Assert.Equal("/var/log", path)
                        });
                    }
                },
                new VolumeInstructionParseTestScenario
                {
                    Text = "VOLUME /var/log /var/db",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "VOLUME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "/var/log"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "/var/db")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("VOLUME", result.InstructionName);
                        Assert.Collection(result.Paths, new Action<string>[]
                        {
                            path => Assert.Equal("/var/log", path),
                            path => Assert.Equal("/var/db", path)
                        });
                    }
                },
                new VolumeInstructionParseTestScenario
                {
                    Text = "VOLUME [\"/var/log\"]",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "VOLUME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/var/log", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']')
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("VOLUME", result.InstructionName);
                        Assert.Collection(result.Paths, new Action<string>[]
                        {
                            path => Assert.Equal("/var/log", path)
                        });
                    }
                },
                new VolumeInstructionParseTestScenario
                {
                    Text = "VOLUME [\"/var/log\", \"/var/db\"]",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "VOLUME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/var/log", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "/var/db", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']')
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("VOLUME", result.InstructionName);
                        Assert.Collection(result.Paths, new Action<string>[]
                        {
                            path => Assert.Equal("/var/log", path),
                            path => Assert.Equal("/var/db", path)
                        });
                    }
                },
                new VolumeInstructionParseTestScenario
                {
                    Text = "VOLUME $TEST",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "VOLUME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LiteralToken>(token, "$TEST",
                            token => ValidateAggregate<VariableRefToken>(token, "$TEST",
                                token => ValidateString(token, "TEST")))
                    }
                },
                new VolumeInstructionParseTestScenario
                {
                    Text = "VOLUME /var/log `\n#test comment\n/var/db",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "VOLUME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "/var/log"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateAggregate<CommentToken>(token, "#test comment\n",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateString(token, "test comment"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateLiteral(token, "/var/db")
                    },
                    Validate = result =>
                    {
                        Assert.Single(result.Comments);
                        Assert.Equal("test comment", result.Comments.First());
                        Assert.Equal("VOLUME", result.InstructionName);
                        Assert.Collection(result.Paths, new Action<string>[]
                        {
                            path => Assert.Equal("/var/log", path),
                            path => Assert.Equal("/var/db", path)
                        });
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public static IEnumerable<object[]> CreateTestInput()
        {
            CreateTestScenario[] testInputs = new CreateTestScenario[]
            {
                new CreateTestScenario
                {
                    Paths = new string[]
                    {
                        "/var/log"
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "VOLUME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/var/log", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']')
                    }
                },
                new CreateTestScenario
                {
                    Paths = new string[]
                    {
                        "/var/log",
                        "/var/db"
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "VOLUME"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "/var/log", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "/var/db", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']')
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class VolumeInstructionParseTestScenario : ParseTestScenario<VolumeInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<VolumeInstruction>
        {
            public IEnumerable<string> Paths { get; set; }
        }
    }
}
