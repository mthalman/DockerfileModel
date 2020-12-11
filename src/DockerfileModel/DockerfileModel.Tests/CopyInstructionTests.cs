using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class CopyInstructionTests : FileTransferInstructionTests<CopyInstruction>
    {
        public CopyInstructionTests()
            : base("COPY", CopyInstruction.Parse,
                  (sources, destination, changeOwner, escapeChar) =>
                    new CopyInstruction(sources, destination, changeOwner: changeOwner, escapeChar: escapeChar))
        {
        }

        [Theory]
        [MemberData(nameof(ParseTestInputBase))]
        public void ParseBase(FileTransferInstructionParseTestScenario scenario) => RunParseTest(scenario);

        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(CopyInstructionParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                CopyInstruction result = CopyInstruction.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate?.Invoke(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => CopyInstruction.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInputBase))]
        public void CreateBase(CreateTestScenario scenario) => RunCreateTest(scenario);

        [Fact]
        public void FromStageName()
        {
            static void Validate(CopyInstruction instruction, string stage)
            {
                Assert.Equal(stage, instruction.FromStageName);
                Assert.Equal(stage, instruction.FromStageNameToken.Value);
                Assert.Equal($"COPY --from={stage} src dst", instruction.ToString());
            }

            CopyInstruction instruction = new CopyInstruction(new string[] { "src" }, "dst", "test", escapeChar: Dockerfile.DefaultEscapeChar);
            Validate(instruction, "test");

            instruction.FromStageName = "test2";
            Validate(instruction, "test2");

            instruction.FromStageName = null;
            Assert.Null(instruction.FromStageName);
            Assert.Null(instruction.FromStageNameToken);
            Assert.Equal($"COPY src dst", instruction.ToString());

            instruction = CopyInstruction.Parse($"COPY`\n src dst", '`');
            instruction.FromStageName = "test3";
            Assert.Equal("test3", instruction.FromStageName);
            Assert.Equal($"COPY --from=test3`\n src dst", instruction.ToString());

            instruction = CopyInstruction.Parse($"COPY`\n --from=stage`\n src dst", '`');
            instruction.FromStageName = null;
            Assert.Null(instruction.FromStageName);
            Assert.Null(instruction.FromStageNameToken);
            Assert.Equal($"COPY`\n`\n src dst", instruction.ToString());
        }

        public static IEnumerable<object[]> ParseTestInputBase() => ParseTestInput("COPY");

        public static IEnumerable<object[]> CreateTestInputBase() => CreateTestInput("COPY");

        public static IEnumerable<object[]> ParseTestInput()
        {
            CopyInstructionParseTestScenario[] testInputs = new CopyInstructionParseTestScenario[]
            {
                new CopyInstructionParseTestScenario
                {
                    Text = $"COPY --from=stage src dst",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "COPY"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateFromFlag(token, "from", "stage"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("COPY", result.InstructionName);
                        Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                        Assert.Equal("dst", result.Destination);
                        Assert.Equal("stage", result.FromStageName);
                    }
                },
                new CopyInstructionParseTestScenario
                {
                    Text = $"COPY --from=stage --chown=id src dst",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "COPY"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateFromFlag(token, "from", "stage"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateAggregate<ChangeOwnerFlag>(token, "--chown=id",
                            token => ValidateSymbol(token, '-'),
                            token => ValidateSymbol(token, '-'),
                            token => ValidateKeyword(token, "chown"),
                            token => ValidateSymbol(token, '='),
                            token => ValidateAggregate<ChangeOwner>(token, "id",
                                token => ValidateLiteral(token, "id"))),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "src"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "dst")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("COPY", result.InstructionName);
                        Assert.Equal(new string[] { "src" }, result.Sources.ToArray());
                        Assert.Equal("dst", result.Destination);
                        Assert.Equal("stage", result.FromStageName);
                        Assert.Equal("id", result.ChangeOwner.User);
                    }
                },
            };

            return testInputs.Select(input => new object[] { input });
        }

        private static void ValidateFromFlag(Token token, string key, string value)
        {
            ValidateAggregate<FromFlag>(token, $"--{key}={value}",
                token => ValidateSymbol(token, '-'),
                token => ValidateSymbol(token, '-'),
                token => ValidateKeyword(token, key),
                token => ValidateSymbol(token, '='),
                token => ValidateIdentifier<StageName>(token, value));
        }

        public class CopyInstructionParseTestScenario : ParseTestScenario<CopyInstruction>
        {
            public char EscapeChar { get; set; }
        }
    }
}
