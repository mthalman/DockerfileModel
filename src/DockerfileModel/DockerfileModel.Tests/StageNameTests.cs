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

        [Fact]
        public void Stage()
        {
            StageName stageName = StageName.Create("test");
            Assert.Equal("test", stageName.Stage);
            Assert.Equal("test", stageName.StageToken.Value);

            stageName.Stage = "test2";
            Assert.Equal("test2", stageName.Stage);
            Assert.Equal("test2", stageName.StageToken.Value);

            stageName.StageToken.Value = "test3";
            Assert.Equal("test3", stageName.Stage);
            Assert.Equal("test3", stageName.StageToken.Value);

            stageName.StageToken = new IdentifierToken("test4");
            Assert.Equal("test4", stageName.Stage);
            Assert.Equal("test4", stageName.StageToken.Value);

            Assert.Throws<ArgumentNullException>(() => stageName.Stage = null);
            Assert.Throws<ArgumentNullException>(() => stageName.StageToken = null);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            StageNameParseTestScenario[] testInputs = new StageNameParseTestScenario[]
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
                            token => ValidateString(token, "comment")),
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
            CreateTestScenario[] testInputs = new CreateTestScenario[]
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
