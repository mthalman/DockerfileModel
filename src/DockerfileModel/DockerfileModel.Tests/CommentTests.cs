using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class CommentTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ParseTestScenario<Comment> scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                Comment result = Comment.Parse(scenario.Text);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => Comment.Parse(scenario.Text));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            Comment result = Comment.Create(scenario.Comment);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate(result);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            var testInputs = new ParseTestScenario<Comment>[]
            {
                new ParseTestScenario<Comment>
                {
                    Text = "#mycomment",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<CommentToken>(token, "#mycomment",
                            token => ValidateSymbol(token, "#"),
                            token => ValidateLiteral(token, "mycomment"))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("mycomment", result.Text);

                        result.Text += "2  ";
                        Assert.Equal($"#mycomment2  ", result.ToString());
                    }
                },
                new ParseTestScenario<Comment>
                {
                    Text = "#mycomment\n",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<CommentToken>(token, "#mycomment",
                            token => ValidateSymbol(token, "#"),
                            token => ValidateLiteral(token, "mycomment")),
                        token => ValidateNewLine(token, "\n")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("mycomment", result.Text);

                        result.Text += "2  ";
                        Assert.Equal($"#mycomment2  \n", result.ToString());
                    }
                },
                new ParseTestScenario<Comment>
                {
                    Text = " \t#\tmycomment\t  ",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateWhitespace(token, " \t"),
                        token => ValidateAggregate<CommentToken>(token, "#\tmycomment\t  ",
                            token => ValidateSymbol(token, "#"),
                            token => ValidateWhitespace(token, "\t"),
                            token => ValidateLiteral(token, "mycomment"),
                            token => ValidateWhitespace(token, "\t  "))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("mycomment", result.Text);

                        result.Text += "2  ";
                        Assert.Equal($" \t#\tmycomment2  \t  ", result.ToString());
                    }
                },
                new ParseTestScenario<Comment>
                {
                    Text = "comment",
                    ParseExceptionPosition = new Position(0, 1, 1)
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public static IEnumerable<object[]> CreateTestInput()
        {
            var testInputs = new CreateTestScenario[]
            {
                new CreateTestScenario
                {
                    Comment = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<CommentToken>(token, "# test",
                            token => ValidateSymbol(token, "#"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "test")),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("test", result.Text);

                        result.Text = "override";
                        Assert.Equal("# override", result.ToString());
                    }
                },
                new CreateTestScenario
                {
                    Comment = "comment   ",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<CommentToken>(token, "# comment   ",
                            token => ValidateSymbol(token, "#"),
                            token => ValidateWhitespace(token, " "),
                            token => ValidateLiteral(token, "comment"),
                            token => ValidateWhitespace(token, "   "))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("comment", result.Text);

                        result.Text = "newcomment";
                        Assert.Equal($"# newcomment   ", result.ToString());
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public class CreateTestScenario : TestScenario<Comment>
        {
            public string Comment { get; set; }
        }
    }
}
