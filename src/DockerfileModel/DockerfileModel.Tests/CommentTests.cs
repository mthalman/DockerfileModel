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
        [MemberData(nameof(CreateFromRawTextTestInput))]
        public void CreateFromRawText(CreateFromRawTextTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                Comment result = Comment.CreateFromRawText(scenario.Text);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => Comment.CreateFromRawText(scenario.Text));
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

        public static IEnumerable<object[]> CreateFromRawTextTestInput()
        {
            var testInputs = new CreateFromRawTextTestScenario[]
            {
                new CreateFromRawTextTestScenario
                {
                    Text = "#mycomment",
                    TokenValidators = new Action<Token>[]
                    {
                        ValidateComment,
                        token => ValidateCommentText(token, "mycomment")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("mycomment", result.Text.Value);

                        result.Text.Value += "2  ";
                        Assert.Equal($"#mycomment2  ", result.ToString());
                    }
                },
                new CreateFromRawTextTestScenario
                {
                    Text = " \t#\tmycomment\t  ",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateWhitespace(token, " \t"),
                        ValidateComment,
                        token => ValidateWhitespace(token, "\t"),
                        token => ValidateCommentText(token, "mycomment"),
                        token => ValidateWhitespace(token, "\t  ")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("mycomment", result.Text.Value);

                        result.Text.Value += "2  ";
                        Assert.Equal($" \t#\tmycomment2  \t  ", result.ToString());
                    }
                },
                new CreateFromRawTextTestScenario
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
                        ValidateComment,
                        token => ValidateWhitespace(token, " "),
                        token => ValidateCommentText(token, "test"),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("test", result.Text.Value);

                        result.Text.Value = "override";
                        Assert.Equal("# override", result.ToString());
                    }
                },
                new CreateTestScenario
                {
                    Comment = "comment   ",
                    TokenValidators = new Action<Token>[]
                    {
                        ValidateComment,
                        token => ValidateWhitespace(token, " "),
                        token => ValidateCommentText(token, "comment"),
                        token => ValidateWhitespace(token, "   "),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("comment", result.Text.Value);

                        result.Text.Value = "newcomment";
                        Assert.Equal($"# newcomment   ", result.ToString());
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public abstract class TestScenario
        {
            public Action<Comment> Validate { get; set; }
            public Action<Token>[] TokenValidators { get; set; }
        }

        public class CreateFromRawTextTestScenario : TestScenario
        {
            public string Text { get; set; }
            public Position ParseExceptionPosition { get; set; }
        }

        public class CreateTestScenario : TestScenario
        {
            public string Comment { get; set; }
        }
    }
}
