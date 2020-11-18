using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public abstract class FileTransferInstructionTests<TInstruction>
        where TInstruction : FileTransferInstruction
    {
        private readonly string instructionName;
        private readonly Func<string, char, TInstruction> parse;
        private readonly Func<IEnumerable<string>, string, ChangeOwner, char, TInstruction> create;

        public FileTransferInstructionTests(
            string instructionName,
            Func<string, char, TInstruction> parse,
            Func<IEnumerable<string>, string, ChangeOwner, char, TInstruction> create)
        {
            this.instructionName = instructionName;
            this.parse = parse;
            this.create = create;
        }

        [Fact]
        public void Sources()
        {
            TInstruction instruction = this.create(new string[] { "src1", "src2" }, "dst", null, Dockerfile.DefaultEscapeChar);
            Assert.Equal(new string[] { "src1", "src2" }, instruction.Sources);
            Assert.Equal(new string[] { "src1", "src2" }, instruction.SourceTokens.Select(token => token.Value).ToArray());

            instruction.Sources[1] = "test2";
            Assert.Equal(new string[] { "src1", "test2" }, instruction.Sources);
            Assert.Equal(new string[] { "src1", "test2" }, instruction.SourceTokens.Select(token => token.Value).ToArray());

            instruction.SourceTokens[0] = new LiteralToken("test1");
            Assert.Equal(new string[] { "test1", "test2" }, instruction.Sources);
            Assert.Equal(new string[] { "test1", "test2" }, instruction.SourceTokens.Select(token => token.Value).ToArray());

            instruction.SourceTokens[1].Value = "foo";
            Assert.Equal(new string[] { "test1", "foo" }, instruction.Sources);
            Assert.Equal(new string[] { "test1", "foo" }, instruction.SourceTokens.Select(token => token.Value).ToArray());
        }

        [Fact]
        public void Destination()
        {
            TInstruction instruction = this.create(new string[] { "src1", "src2" }, "dst", null, Dockerfile.DefaultEscapeChar);
            Assert.Equal("dst", instruction.Destination);
            Assert.Equal("dst", instruction.DestinationToken.Value);

            instruction.Destination = "test";
            Assert.Equal("test", instruction.Destination);
            Assert.Equal("test", instruction.DestinationToken.Value);

            instruction.DestinationToken.Value = "foo";
            Assert.Equal("foo", instruction.Destination);
            Assert.Equal("foo", instruction.DestinationToken.Value);

            instruction.DestinationToken = new LiteralToken("bar");
            Assert.Equal("bar", instruction.Destination);
            Assert.Equal("bar", instruction.DestinationToken.Value);

            Assert.Throws<ArgumentNullException>(() => instruction.Destination = null);
            Assert.Throws<ArgumentException>(() => instruction.Destination = "");
            Assert.Throws<ArgumentNullException>(() => instruction.DestinationToken = null);
        }

        [Fact]
        public void ChangeOwner()
        {
            void Validate(TInstruction instruction, string user)
            {
                Assert.Equal(user, instruction.ChangeOwner.User);
                Assert.Equal($"{instructionName} --chown={user} src dst", instruction.ToString());
            }

            ChangeOwner changeOwner = DockerfileModel.ChangeOwner.Create("user");
            TInstruction instruction = this.create(new string[] { "src" }, "dst", changeOwner, Dockerfile.DefaultEscapeChar);
            Validate(instruction, "user");

            instruction.ChangeOwner = DockerfileModel.ChangeOwner.Create("user2");
            Validate(instruction, "user2");

            instruction.ChangeOwner = null;
            Assert.Null(instruction.ChangeOwner);
            Assert.Equal($"{instructionName} src dst", instruction.ToString());

            instruction = this.parse($"{instructionName}`\n src dst", '`');
            instruction.ChangeOwner = DockerfileModel.ChangeOwner.Create("user");
            Assert.Equal("user", instruction.ChangeOwner.User);
            Assert.Equal($"{instructionName} --chown=user`\n src dst", instruction.ToString());

            instruction = this.parse($"{instructionName}`\n --chown=user`\n src dst", '`');
            instruction.ChangeOwner = null;
            Assert.Null(instruction.ChangeOwner);
            Assert.Equal($"{instructionName}`\n`\n src dst", instruction.ToString());
        }

        protected void RunParseTest(FileTransferInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                TInstruction result = this.parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => AddInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        protected void RunCreateTest(CreateTestScenario scenario)
        {
            TInstruction result = this.create(scenario.Sources, scenario.Destination, scenario.ChangeOwner, scenario.EscapeChar);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        public static IEnumerable<object[]> ParseTestInput(string instructionName)
        {
            FileTransferInstructionParseTestScenario[] testInputs = new FileTransferInstructionParseTestScenario[]
            {
                new FileTransferInstructionParseTestScenario
                {
                    Text = $"{instructionName} src dst",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal(instructionName, result.InstructionName);
                        Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                        Assert.Equal("dst", result.Destination);
                    }
                },
                new FileTransferInstructionParseTestScenario
                {
                    Text = $"{instructionName} --chown=1:2 src dst",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=1:2",
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateKeyword(token, "chown"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<ChangeOwner>(token, "1:2",
                                token => ValidateLiteral(token, "1"),
                                token => ValidateSymbol(token, ':'),
                                token => ValidateLiteral(token, "2"))),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal(instructionName, result.InstructionName);
                        Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                        Assert.Equal("dst", result.Destination);
                    }
                },
                new FileTransferInstructionParseTestScenario
                {
                    Text = $"{instructionName} path/to/src1.txt src2 my/dst/",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "path/to/src1.txt"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src2"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "my/dst/")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal(instructionName, result.InstructionName);
                        Assert.Equal(new string[] { "path/to/src1.txt", "src2" }, result.Sources.ToArray());
                        Assert.Equal("my/dst/", result.Destination);
                    }
                },
                new FileTransferInstructionParseTestScenario
                {
                    Text = $"{instructionName} $src dst",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<LiteralToken>(token, "$src",
                            token => ValidateAggregate<VariableRefToken>(token, "$src",
                                token => ValidateString(token, "src"))),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst")
                    }
                },
                new FileTransferInstructionParseTestScenario
                {
                    Text = $"{instructionName} [\"$src\", \"dst\"]",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, '['),
                        token => ValidateQuotableAggregate<LiteralToken>(token, "\"$src\"", ParseHelper.DoubleQuote,
                            token => ValidateAggregate<VariableRefToken>(token, "$src",
                                token => ValidateString(token, "src"))),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']')
                    }
                },
                new FileTransferInstructionParseTestScenario
                {
                    Text = $"{instructionName} s\\$rc dst",
                    EscapeChar = '\\',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "s\\$rc"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst")
                    }
                },
                new FileTransferInstructionParseTestScenario
                {
                    Text = $"{instructionName} src `\n#test comment\ndst",
                    EscapeChar = '`',
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLineContinuation(token, '`', "\n"),
                        token => ValidateAggregate<CommentToken>(token, "#test comment\n",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateString(token, "test comment"),
                            token => ValidateNewLine(token, "\n")),
                        token => ValidateLiteral(token, "dst")
                    },
                    Validate = result =>
                    {
                        Assert.Single(result.Comments);
                        Assert.Equal("test comment", result.Comments.First());
                        Assert.Equal(instructionName, result.InstructionName);
                        Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                        Assert.Equal("dst", result.Destination);
                    }
                },
                new FileTransferInstructionParseTestScenario
                {
                    Text = $"{instructionName} [\"source 1.txt\", \"path/to/source 2.txt\", \"/my dst/\"]",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "source 1.txt", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "path/to/source 2.txt", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "/my dst/", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']')
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal(instructionName, result.InstructionName);
                        Assert.Equal(new string[] { "source 1.txt", "path/to/source 2.txt" }, result.Sources.ToArray());
                        Assert.Equal("/my dst/", result.Destination);
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public static IEnumerable<object[]> CreateTestInput(string instructionName)
        {
            CreateTestScenario[] testInputs = new CreateTestScenario[]
            {
                new CreateTestScenario
                {
                    Sources = new string[]
                    {
                        "src1",
                        "src2"
                    },
                    Destination = "dst",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src1"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src2"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst")
                    }
                },
                new CreateTestScenario
                {
                    Sources = new string[]
                    {
                        "src 1.txt",
                        "my path/to/src2"
                    },
                    Destination = "dst",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, '['),
                        token => ValidateLiteral(token, "src 1.txt", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "my path/to/src2", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ','),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst", ParseHelper.DoubleQuote),
                        token => ValidateSymbol(token, ']')
                    }
                },

                new CreateTestScenario
                {
                    Sources = new string[]
                    {
                        "src1",
                        "src2"
                    },
                    Destination = "dst",
                    ChangeOwner = DockerfileModel.ChangeOwner.Create("user", "group"),
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, instructionName),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=user:group",
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateKeyword(token, "chown"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<ChangeOwner>(token, "user:group",
                                token => ValidateLiteral(token, "user"),
                                token => ValidateSymbol(token, ':'),
                                token => ValidateLiteral(token, "group"))),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src1"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src2"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst")
                    }
                },
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class FileTransferInstructionParseTestScenario : ParseTestScenario<TInstruction>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<TInstruction>
        {
            public string Destination { get; set; }
            public IEnumerable<string> Sources { get; set; }
            public ChangeOwner ChangeOwner { get; set; }
            public char EscapeChar { get; set; } = Dockerfile.DefaultEscapeChar;
        }
    }
}
