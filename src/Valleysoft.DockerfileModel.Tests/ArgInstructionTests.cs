using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests
{
    public class ArgInstructionTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ArgInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ArgInstruction result = ArgInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => ArgInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            ArgInstruction result = new ArgInstruction(scenario.Args);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        [Fact]
        public void Args()
        {
            void Validate(ArgInstruction instruction, string expectedKey, string expectedValue)
            {
                Assert.Collection(instruction.Args, new Action<IKeyValuePair>[]
                {
                    pair =>
                    {
                        Assert.Equal(expectedKey, pair.Key);
                        Assert.Equal(expectedValue, pair.Value);
                    }
                });

                Assert.Collection(instruction.ArgTokens, new Action<ArgDeclaration>[]
                {
                    token => ValidateAggregate<ArgDeclaration>(token, $"{expectedKey}={expectedValue}",
                        token => ValidateIdentifier<Variable>(token, expectedKey),
                        token => ValidateSymbol(token, '='),
                        token => ValidateLiteral(token, expectedValue))
                });
            }

            ArgInstruction result = new ArgInstruction(
                new Dictionary<string, string>
                {
                    { "VAR1", "test" }
                });
            Validate(result, "VAR1", "test");

            result.Args[0].Key = "VAR2";
            Validate(result, "VAR2", "test");

            result.Args[0].Value = "foo";
            Validate(result, "VAR2", "foo");

            result.ArgTokens[0].Name = "VAR3";
            Validate(result, "VAR3", "foo");

            result.ArgTokens[0].Value = "bar";
            Validate(result, "VAR3", "bar");
        }
        public static IEnumerable<object[]> ParseTestInput()
        {
            ArgInstructionParseTestScenario[] testInputs = new ArgInstructionParseTestScenario[]
            {
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG",
                            token => ValidateIdentifier<Variable>(token, "MYARG"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Single(result.Args);
                        Assert.Single(result.ArgTokens);
                        Assert.Equal("MYARG", result.Args[0].Key);
                        Assert.Null(result.Args[0].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG1 MYARG2",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG1",
                            token => ValidateIdentifier<Variable>(token, "MYARG1")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG2",
                            token => ValidateIdentifier<Variable>(token, "MYARG2"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal(2, result.Args.Count);
                        Assert.Equal(2, result.ArgTokens.Count);
                        Assert.Equal("MYARG1", result.Args[0].Key);
                        Assert.Null(result.Args[0].Value);
                        Assert.Equal("MYARG2", result.Args[1].Key);
                        Assert.Null(result.Args[1].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG `\nMYARG",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG",
                            token => ValidateIdentifier<Variable>(token, "MYARG"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Single(result.Args);
                        Assert.Single(result.ArgTokens);
                        Assert.Equal("MYARG", result.Args[0].Key);
                        Assert.Null(result.Args[0].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG=",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG=",
                            token => ValidateIdentifier<Variable>(token, "MYARG"),
                            token => ValidateSymbol(token, '='))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Single(result.Args);
                        Assert.Single(result.ArgTokens);
                        Assert.Equal("MYARG", result.Args[0].Key);
                        Assert.Equal("", result.Args[0].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG1= MYARG2=",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG1=",
                            token => ValidateIdentifier<Variable>(token, "MYARG1"),
                            token => ValidateSymbol(token, '=')),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG2=",
                            token => ValidateIdentifier<Variable>(token, "MYARG2"),
                            token => ValidateSymbol(token, '=')),
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal(2, result.Args.Count);
                        Assert.Equal(2, result.ArgTokens.Count);
                        Assert.Equal("MYARG1", result.Args[0].Key);
                        Assert.Equal("", result.Args[0].Value);
                        Assert.Equal("MYARG2", result.Args[1].Key);
                        Assert.Equal("", result.Args[1].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG=\"\"",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG=\"\"",
                            token => ValidateIdentifier<Variable>(token, "MYARG"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "", '\"'))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Single(result.Args);
                        Assert.Single(result.ArgTokens);
                        Assert.Equal("MYARG", result.Args[0].Key);
                        Assert.Equal("", result.Args[0].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG MYARG1=\"\" MYARG2=\"\"",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG1=\"\"",
                            token => ValidateIdentifier<Variable>(token, "MYARG1"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "", '\"')),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG2=\"\"",
                            token => ValidateIdentifier<Variable>(token, "MYARG2"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "", '\"')),
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal(2, result.Args.Count);
                        Assert.Equal(2, result.ArgTokens.Count);
                        Assert.Equal("MYARG1", result.Args[0].Key);
                        Assert.Equal("", result.Args[0].Value);
                        Assert.Equal("MYARG2", result.Args[1].Key);
                        Assert.Equal("", result.Args[1].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG `\n# my comment\n  MYARG=",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LineContinuationToken>(token, "`\n",
                            token => ValidateSymbol(token, '`'),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateAggregate<CommentToken>(token, "# my comment\n",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateString(token, "my comment"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateWhitespace(token, "  "),
                        token => ValidateAggregate<ArgDeclaration>(token, "MYARG=",
                            token => ValidateIdentifier<Variable>(token, "MYARG"),
                            token => ValidateSymbol(token, '='))
                    },
                    Validate = result =>
                    {
                        Assert.Collection(result.Comments,
                            comment => Assert.Equal("my comment", comment));
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Single(result.Args);
                        Assert.Single(result.ArgTokens);
                        Assert.Equal("MYARG", result.Args[0].Key);
                        Assert.Equal("", result.Args[0].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG myarg=1",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "myarg=1",
                            token => ValidateIdentifier<Variable>(token, "myarg"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "1"))
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Single(result.Args);
                        Assert.Single(result.ArgTokens);
                        Assert.Equal("myarg", result.Args[0].Key);
                        Assert.Equal("1", result.Args[0].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG myarg1=1 myarg2=2",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "myarg1=1",
                            token => ValidateIdentifier<Variable>(token, "myarg1"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "1")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "myarg2=2",
                            token => ValidateIdentifier<Variable>(token, "myarg2"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "2")),
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("ARG", result.InstructionName);
                        Assert.Equal(2, result.Args.Count);
                        Assert.Equal(2, result.ArgTokens.Count);
                        Assert.Equal("myarg1", result.Args[0].Key);
                        Assert.Equal("1", result.Args[0].Value);
                        Assert.Equal("myarg2", result.Args[1].Key);
                        Assert.Equal("2", result.Args[1].Value);
                    }
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "xARG ",
                    ParseExceptionPosition = new Position(1, 1, 1)
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG ",
                    ParseExceptionPosition = new Position(1, 1, 5)
                },
                new ArgInstructionParseTestScenario
                {
                    Text = "ARG =",
                    ParseExceptionPosition = new Position(1, 1, 5)
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
                    Args = new Dictionary<string, string>
                    {
                        { "TEST1", null }
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "TEST1",
                            token => ValidateIdentifier<Variable>(token, "TEST1"))
                    }
                },
                new CreateTestScenario
                {
                    Args = new Dictionary<string, string>
                    {
                        { "TEST1", null },
                        { "TEST2", null }
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "TEST1",
                            token => ValidateIdentifier<Variable>(token, "TEST1")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "TEST2",
                            token => ValidateIdentifier<Variable>(token, "TEST2"))
                    }
                },
                new CreateTestScenario
                {
                    Args = new Dictionary<string, string>
                    {
                        { "TEST1", "b" }
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "TEST1=b",
                            token => ValidateIdentifier<Variable>(token, "TEST1"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "b"))
                    }
                },
                new CreateTestScenario
                {
                    Args = new Dictionary<string, string>
                    {
                        { "TEST1", "b" },
                        { "TEST2", "c" }
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "TEST1=b",
                            token => ValidateIdentifier<Variable>(token, "TEST1"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "b")),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "TEST2=c",
                            token => ValidateIdentifier<Variable>(token, "TEST2"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateLiteral(token, "c"))
                    }
                },
                new CreateTestScenario
                {
                    Args = new Dictionary<string, string>
                    {
                        { "TEST1", "" }
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "TEST1=",
                            token => ValidateIdentifier<Variable>(token, "TEST1"),
                            token => ValidateSymbol(token, '='))
                    }
                },
                new CreateTestScenario
                {
                    Args = new Dictionary<string, string>
                    {
                        { "TEST1", "" },
                        { "TEST2", "" }
                    },
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "ARG"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "TEST1=",
                            token => ValidateIdentifier<Variable>(token, "TEST1"),
                            token => ValidateSymbol(token, '=')),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ArgDeclaration>(token, "TEST2=",
                            token => ValidateIdentifier<Variable>(token, "TEST2"),
                            token => ValidateSymbol(token, '='))
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class ArgInstructionParseTestScenario : ParseTestScenario<ArgInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<ArgInstruction>
        {
            public Dictionary<string, string> Args { get; set; }
        }
    }
}
