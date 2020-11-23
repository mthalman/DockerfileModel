using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
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
            Comment result = new Comment(scenario.Comment);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate(result);
        }

        [Fact]
        public void Text()
        {
            Comment comment = new Comment("test");
            Assert.Equal("test", comment.Value);
            Assert.Equal("test", comment.ValueToken.Text);

            comment.Value = "test2";
            Assert.Equal("test2", comment.Value);
            Assert.Equal("test2", comment.ValueToken.Text);

            comment.Value = "";
            Assert.Null(comment.Value);
            Assert.Null(comment.ValueToken.Text);

            comment.Value = "test2";

            comment.Value = null;
            Assert.Null(comment.Value);
            Assert.Null(comment.ValueToken.Text);

            Assert.Throws<ArgumentNullException>(() => comment.ValueToken = null);
        }

        public static IEnumerable<object[]> ParseTestInput()
        {
            ParseTestScenario<Comment>[] testInputs = new ParseTestScenario<Comment>[]
            {
                new ParseTestScenario<Comment>
                {
                    Text = "#mycomment",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<CommentToken>(token, "#mycomment",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateString(token, "mycomment"))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("mycomment", result.Value);

                        result.Value += "2  ";
                        Assert.Equal($"#mycomment2  ", result.ToString());
                    }
                },
                new ParseTestScenario<Comment>
                {
                    Text = "#mycomment\n",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<CommentToken>(token, "#mycomment\n",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateString(token, "mycomment"),
                            token => ValidateNewLine(token, "\n"))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("mycomment", result.Value);

                        result.Value += "2  ";
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
                            token => ValidateSymbol(token, '#'),
                            token => ValidateWhitespace(token, "\t"),
                            token => ValidateString(token, "mycomment"),
                            token => ValidateWhitespace(token, "\t  "))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("mycomment", result.Value);

                        result.Value += "2  ";
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
            CreateTestScenario[] testInputs = new CreateTestScenario[]
            {
                new CreateTestScenario
                {
                    Comment = "test",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<CommentToken>(token, "#test",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateString(token, "test")),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("test", result.Value);

                        result.Value = "override";
                        Assert.Equal("#override", result.ToString());
                    }
                },
                new CreateTestScenario
                {
                    Comment = "comment   ",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateAggregate<CommentToken>(token, "#comment   ",
                            token => ValidateSymbol(token, '#'),
                            token => ValidateString(token, "comment"),
                            token => ValidateWhitespace(token, "   "))
                    },
                    Validate = result =>
                    {
                        Assert.Equal("comment", result.Value);

                        result.Value = "newcomment";
                        Assert.Equal($"#newcomment   ", result.ToString());
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
