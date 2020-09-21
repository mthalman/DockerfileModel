using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Sprache;
using Xunit;

using static DockerfileModel.Tests.TokenValidator;

namespace DockerfileModel.Tests
{
    public class ParserDirectiveTests
    {
        [Theory]
        [MemberData(nameof(ParseTestInput))]
        public void Parse(ParseTestScenario scenario)
        {
            if (scenario.ParseExceptionPosition is null)
            {
                ParserDirective result = ParserDirective.Parse(scenario.Text);
                Assert.Equal(scenario.Text, result.ToString());
                Assert.Collection(result.Tokens, scenario.TokenValidators);
                scenario.Validate(result);
            }
            else
            {
                ParseException exception = Assert.Throws<ParseException>(
                    () => ParserDirective.Parse(scenario.Text));
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

        public static IEnumerable<object[]> ParseTestInput()
        {
            ParseTestScenario[] testInputs = new ParseTestScenario[]
            {
                new ParseTestScenario
                {
                    Text = "#directive=value",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, "#"),
                        token => ValidateKeyword(token, "directive"),
                        token => ValidateSymbol(token, "="),
                        token => ValidateLiteral(token, "value")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("directive", result.DirectiveName.Value);
                        Assert.Equal("value", result.DirectiveValue.Value);

                        result.DirectiveName.Value = "directive2";
                        result.DirectiveValue.Value = "newvalue";
                        Assert.Equal($"#{result.DirectiveName.Value}={result.DirectiveValue.Value}", result.ToString());
                    }
                },
                new ParseTestScenario
                {
                    Text = " # directive   = value  ",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateWhitespace(token, " "),
                        token => ValidateSymbol(token, "#"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyword(token, "directive"),
                        token => ValidateWhitespace(token, "   "),
                        token => ValidateSymbol(token, "="),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "value"),
                        token => ValidateWhitespace(token, "  "),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("directive", result.DirectiveName.Value);
                        Assert.Equal("value", result.DirectiveValue.Value);

                        result.DirectiveName.Value = "directive2";
                        result.DirectiveValue.Value = "newvalue";
                        Assert.Equal($" # {result.DirectiveName.Value}   = {result.DirectiveValue.Value}  ", result.ToString());
                    }
                },
                new ParseTestScenario
                {
                    Text = "#comment",
                    ParseExceptionPosition = new Position(0, 1, 9)
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
                    Directive = "directive",
                    Value = "value",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, "#"),
                        token => ValidateKeyword(token, "directive"),
                        token => ValidateSymbol(token, "="),
                        token => ValidateLiteral(token, "value")
                    },
                    Validate = result =>
                    {
                        Assert.Equal("directive", result.DirectiveName.Value);
                        Assert.Equal("value", result.DirectiveValue.Value);

                        result.DirectiveName.Value = "directive2";
                        result.DirectiveValue.Value = "newvalue";
                        Assert.Equal($"#{result.DirectiveName.Value}={result.DirectiveValue.Value}", result.ToString());
                    }
                },
                new CreateTestScenario
                {
                    Directive = " directive   ",
                    Value = " value  ",
                    TokenValidators = new Action<Token>[]
                    {
                        token => ValidateSymbol(token, "#"),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateKeyword(token, "directive"),
                        token => ValidateWhitespace(token, "   "),
                        token => ValidateSymbol(token, "="),
                        token => ValidateWhitespace(token, " "),
                        token => ValidateLiteral(token, "value"),
                        token => ValidateWhitespace(token, "  "),
                    },
                    Validate = result =>
                    {
                        Assert.Equal("directive", result.DirectiveName.Value);
                        Assert.Equal("value", result.DirectiveValue.Value);

                        result.DirectiveName.Value = "directive2";
                        result.DirectiveValue.Value = "newvalue";
                        Assert.Equal($"# {result.DirectiveName.Value}   = {result.DirectiveValue.Value}  ", result.ToString());
                    }
                }
            };

            return testInputs.Select(input => new object[] { input });
        }

        public abstract class TestScenario
        {
            public Action<ParserDirective> Validate { get; set; }
            public Action<Token>[] TokenValidators { get; set; }
        }

        public class ParseTestScenario : TestScenario
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
