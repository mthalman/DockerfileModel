using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;
using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class StageNameTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(StageNameParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                StageName result = StageName.Parse(scenario.Text, scenario.EscapeChar);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => StageName.Parse(scenario.Text, scenario.EscapeChar));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            StageName result = StageName.Create(scenario.Stage);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            var testInputs = new StageNameParseTestScenario[]
            {
                new StageNameParseTestScenario
                {
                    Text = "AS installer",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "AS"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "installer")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("installer", result.Stage);
                    }
                },
                new StageNameParseTestScenario
                {
                    Text = "AS \n#comment\n installer",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "AS"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateAggregate<CommentToken>(token, "#comment",
                            token => ValidateSymbol(token, "#"),
                            token => ValidateLiteral(token, "comment")),
                        token => ValidateNewLine(token, "\n"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "installer")
                    },
                    Validate = result =>
                    {
                        Assert.Single(result.Comments);
                        Assert.Equal("comment", result.Comments.First());
                        Assert.Equal("installer", result.Stage);
                    }
                },
                new StageNameParseTestScenario
                {
                    Text = "AS st-a_g.e",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "AS"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "st-a_g.e")
                    },
                    Validate = result =>
                    {
                        Assert.Empty(result.Comments);
                        Assert.Equal("st-a_g.e", result.Stage);
                    }
                },
            };

            return testInputs.Select(input => new object[] { input });
        }

        public static IEnumerable<object[]> CreateTestInput()
        {
            var testInputs = new CreateTestScenario[]
            {
                new CreateTestScenario
                {
                    Stage = "stage_name",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateKeyword(token, "AS"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateIdentifier(token, "stage_name")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("AS stage_name", result.ToString());
                        Assert.Equal("stage_name", result.Stage);

                        result.Stage = "stage.name";
                        Assert.Equal("stage.name", result.Stage);
                        Assert.Equal("AS stage.name", result.ToString());
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class StageNameParseTestScenario : ParseTestScenario<StageName>
        {
            public char EscapeChar { get; set; }
        }

        public class CreateTestScenario : TestScenario<StageName>
        {
            public string Stage { get; set; }
        }
    }
}
