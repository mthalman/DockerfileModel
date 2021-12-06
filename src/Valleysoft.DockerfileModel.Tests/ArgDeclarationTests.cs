using Valleysoft.DockerfileModel.Tokens;
using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class ArgDeclarationTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ArgDeclarationParseTestScenario scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            ArgDeclaration result = ArgDeclaration.Parse(scenario.Text, scenario.EscapeChar);
            Assert.Equal(scenario.Text, result.ToString());
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => ArgDeclaration.Parse(scenario.Text, scenario.EscapeChar));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        ArgDeclaration result = new(scenario.Name, scenario.Value);
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    [Fact]
    public void Name()
    {
        ArgDeclaration arg = new("test");
        Assert.Equal("test", arg.Name);
        Assert.Equal("test", arg.NameToken.Value);

        arg.Name = "test2";
        Assert.Equal("test2", arg.Name);
        Assert.Equal("test2", arg.NameToken.Value);

        arg.NameToken.Value = "test3";
        Assert.Equal("test3", arg.Name);
        Assert.Equal("test3", arg.NameToken.Value);

        Assert.Throws<ArgumentNullException>(() => arg.Name = null);
        Assert.Throws<ArgumentException>(() => arg.Name = "");
        Assert.Throws<ArgumentNullException>(() => arg.NameToken = null);
    }

    [Fact]
    public void Value()
    {
        ArgDeclaration arg = new("test");
        Assert.Null(arg.Value);
        Assert.Null(arg.ValueToken);
        Assert.False(arg.HasAssignmentOperator);

        arg.Value = "foo";
        Assert.Equal("foo", arg.Value);
        Assert.Equal("foo", arg.ValueToken.Value);
        Assert.True(arg.HasAssignmentOperator);

        arg.Value = "";
        Assert.Equal("", arg.Value);
        Assert.Equal("", arg.ValueToken.Value);
        Assert.True(arg.HasAssignmentOperator);

        arg.Value = "foo";

        arg.Value = null;
        Assert.Null(arg.Value);
        Assert.Null(arg.ValueToken);
        Assert.False(arg.HasAssignmentOperator);

        arg.ValueToken = new LiteralToken("foo2");
        Assert.Equal("foo2", arg.Value);
        Assert.Equal("foo2", arg.ValueToken.Value);
        Assert.True(arg.HasAssignmentOperator);

        arg.ValueToken = new LiteralToken("foo3");
        Assert.Equal("foo3", arg.Value);
        Assert.Equal("foo3", arg.ValueToken.Value);
        Assert.True(arg.HasAssignmentOperator);

        arg.ValueToken = null;
        Assert.Null(arg.Value);
        Assert.Null(arg.ValueToken);
        Assert.False(arg.HasAssignmentOperator);
    }

    [Fact]
    public void ValueWithVariables()
    {
        ArgDeclaration arg = new("test", "$var");
        TestHelper.TestVariablesWithNullableLiteral(
            () => arg.ValueToken, token => arg.ValueToken = token, val => arg.Value = val, "var", canContainVariables: true);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        ArgDeclarationParseTestScenario[] testInputs = new ArgDeclarationParseTestScenario[]
        {
            new ArgDeclarationParseTestScenario
            {
                Text = "MYARG",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "MYARG")
                },
                Validate = result =>
                {
                    Assert.Equal("MYARG", result.Name);
                    Assert.Null(result.Value);
                }
            },
            new ArgDeclarationParseTestScenario
            {
                Text = "MYARG=",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "MYARG"),
                    token => ValidateSymbol(token, '=')
                },
                Validate = result =>
                {
                    Assert.Equal("MYARG", result.Name);
                    Assert.Equal("", result.Value);
                }
            },
            new ArgDeclarationParseTestScenario
            {
                Text = "MYARG=\"\"",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "MYARG"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "", '\"')
                },
                Validate = result =>
                {
                    Assert.Equal("MYARG", result.Name);
                    Assert.Equal("", result.Value);
                }
            },
            new ArgDeclarationParseTestScenario
            {
                Text = "myarg=1",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "myarg"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "1")
                },
                Validate = result =>
                {
                    Assert.Equal("myarg", result.Name);
                    Assert.Equal("1", result.Value);
                }
            },
            new ArgDeclarationParseTestScenario
            {
                Text = "myarg`\n=`\n1",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "myarg"),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLineContinuation(token, '`', "\n"),
                    token => ValidateLiteral(token, "1")
                },
                Validate = result =>
                {
                    Assert.Equal("myarg", result.Name);
                    Assert.Equal("1", result.Value);
                }
            },
            new ArgDeclarationParseTestScenario
            {
                Text = "MYARG=\"test\"",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "MYARG"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "test", '\"')
                },
                Validate = result =>
                {
                    Assert.Equal("MYARG", result.Name);
                    Assert.Equal("test", result.Value);
                }
            },
            new ArgDeclarationParseTestScenario
            {
                Text = "\"MY_ARG\"='value'",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "MY_ARG", '\"'),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "value", '\''),
                },
                Validate = result =>
                {
                    Assert.Equal("MY_ARG", result.Name);
                    Assert.Equal("value", result.Value);
                }
            },
            new ArgDeclarationParseTestScenario
            {
                Text = "\"MY`\"_ARG\"='va`'lue'",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "MY`\"_ARG", '\"'),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "va`'lue", '\''),
                },
                Validate = result =>
                {
                    Assert.Equal("MY`\"_ARG", result.Name);
                    Assert.Equal("va`'lue", result.Value);
                }
            },
            new ArgDeclarationParseTestScenario
            {
                Text = "MY_ARG=va`'lue",
                EscapeChar = '`',
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "MY_ARG"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "va`'lue"),
                },
                Validate = result =>
                {
                    Assert.Equal("MY_ARG", result.Name);
                    Assert.Equal("va`'lue", result.Value);
                }
            },
            new ArgDeclarationParseTestScenario
            {
                Text = "MY_ARG=\'\'",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "MY_ARG"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "", '\'')
                },
                Validate = result =>
                {
                    Assert.Equal("MY_ARG", result.Name);
                    Assert.Empty(result.Value);
                }
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
                Name = "TEST1",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "TEST1")
                },
                Validate = result =>
                {
                    Assert.Equal("TEST1", result.Name);
                    Assert.Null(result.Value);

                    result.Name = "TEST2";
                    Assert.Equal("TEST2", result.Name);
                    Assert.Equal("TEST2", result.ToString());

                    result.Value = "a";
                    Assert.Equal("a", result.Value);
                    Assert.Equal("TEST2=a", result.ToString());

                    result.Value = null;
                    Assert.Null(result.Value);
                    Assert.Equal("TEST2", result.ToString());

                    result.Value = "";
                    Assert.Equal("", result.Value);
                    Assert.Equal("TEST2=", result.ToString());
                }
            },
            new CreateTestScenario
            {
                Name = "TEST1",
                Value = "b",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "TEST1"),
                    token => ValidateSymbol(token, '='),
                    token => ValidateLiteral(token, "b")
                }
            },
            new CreateTestScenario
            {
                Name = "TEST1",
                Value = "",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateIdentifier<Variable>(token, "TEST1"),
                    token => ValidateSymbol(token, '=')
                }
            }
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class ArgDeclarationParseTestScenario : ParseTestScenario<ArgDeclaration>
    {
        public char EscapeChar { get; set; }
    }

    public class CreateTestScenario : TestScenario<ArgDeclaration>
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
