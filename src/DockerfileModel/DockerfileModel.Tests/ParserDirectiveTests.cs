using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;
using Xunit;

namespace DockerfileModel.Tests
{
    public class ParserDirectiveTests
    {
        [Theory]
        [MemberData(nameof(CreateFromRawTextTestInput))]
        public void CreateFromRawText(CreateFromRawTextTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ParserDirective result = ParserDirective.CreateFromRawText(scenario.Text);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => ParserDirective.CreateFromRawText(scenario.Text));
                Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
                Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
            }
        }

        [Theory]
        [MemberData(nameof(CreateTestInput))]
        public void Create(CreateTestScenario scenario)
        {
            ParserDirective result = ParserDirective.Create(scenario.Directive, scenario.Value);
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate(result);
        }

        public static IEnumerable<object[]> CreateFromRawTextTestInput()
        {
            var testInputs = new CreateFromRawTextTestScenario[]
            {
                new CreateFromRawTextTestScenario
                {
                    Text = "#directive=value",
                    TokenValidators = new Action<Token>[]
                    {
                        ValidateComment,
                        token => ValidateDirective(token, "directive"),
                        ValidateOperator,
                        token => ValidateValue(token, "value")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("directive", result.Directive.Value);
                        Assert.Equal("value", result.Value.Value);

                        result.Directive.Value = "directive2";
                        result.Value.Value = "newvalue";
                        Assert.Equal($"#{result.Directive.Value}={result.Value.Value}", result.ToString());
                    }
                },
                new CreateFromRawTextTestScenario
                {
                    Text = " # directive   = value  ",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateWhitespace(token, " "),
                        ValidateComment,
                        token => ValidateWhitespace(token, " "),
                        token => ValidateDirective(token, "directive"),
                        token => ValidateWhitespace(token, "   "),
                        ValidateOperator,
                        token => ValidateWhitespace(token, " "),
                        token => ValidateValue(token, "value"),
                        token => ValidateWhitespace(token, "  "),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("directive", result.Directive.Value);
                        Assert.Equal("value", result.Value.Value);

                        result.Directive.Value = "directive2";
                        result.Value.Value = "newvalue";
                        Assert.Equal($" # {result.Directive.Value}   = {result.Value.Value}  ", result.ToString());
                    }
                },
                new CreateFromRawTextTestScenario
                {
                    Text = "#comment",
                    ParseExceptionPosition = new Position(0, 1, 9)
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
                    Directive = "directive",
                    Value = "value",
                    TokenValidators = new Action<Token>[]
                    {
                        ValidateComment,
                        token => ValidateDirective(token, "directive"),
                        ValidateOperator,
                        token => ValidateValue(token, "value")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("directive", result.Directive.Value);
                        Assert.Equal("value", result.Value.Value);

                        result.Directive.Value = "directive2";
                        result.Value.Value = "newvalue";
                        Assert.Equal($"#{result.Directive.Value}={result.Value.Value}", result.ToString());
                    }
                },
                new CreateTestScenario
                {
                    Directive = " directive   ",
                    Value = " value  ",
                    TokenValidators = new Action<Token>[]
                    {
                        ValidateComment,
                        token => ValidateWhitespace(token, " "),
                        token => ValidateDirective(token, "directive"),
                        token => ValidateWhitespace(token, "   "),
                        ValidateOperator,
                        token => ValidateWhitespace(token, " "),
                        token => ValidateValue(token, "value"),
                        token => ValidateWhitespace(token, "  "),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("directive", result.Directive.Value);
                        Assert.Equal("value", result.Value.Value);

                        result.Directive.Value = "directive2";
                        result.Value.Value = "newvalue";
                        Assert.Equal($"# {result.Directive.Value}   = {result.Value.Value}  ", result.ToString());
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        private static void ValidateComment(Token token)
        {
            Assert.IsType<CommentToken>(token);
            Assert.Equal("#", token.Value);
        }

        private static void ValidateDirective(Token token, string directiveName)
        {
            Assert.IsType<KeywordToken>(token);
            Assert.Equal(directiveName, token.Value);
        }

        private static void ValidateValue(Token token, string directiveValue)
        {
            Assert.IsType<LiteralToken>(token);
            Assert.Equal(directiveValue, token.Value);
        }

        private static void ValidateOperator(Token token)
        {
            Assert.IsType<OperatorToken>(token);
            Assert.Equal("=", token.Value);
        }

        private static void ValidateWhitespace(Token token, string whitespace)
        {
            Assert.IsType<WhitespaceToken>(token);
            Assert.Equal(whitespace, token.Value);
        }

        public abstract class TestScenario
        {
            public Action<ParserDirective> Validate { get; set; }
            public Action<Token>[] TokenValidators { get; set; }
        }

        public class CreateFromRawTextTestScenario : TestScenario
        {
            public string Text { get; set; }
            public Position ParseExceptionPosition { get; set; }
        }

        public class CreateTestScenario : TestScenario
        {
            public string Directive { get; set; }
            public string Value { get; set; }
        }
    }
}
