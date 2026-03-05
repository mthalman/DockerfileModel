using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class KeepGitDirFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(KeepGitDirFlagParseTestScenario scenario)
    {
        if (scenario.ParseExceptionPosition is null)
        {
            KeepGitDirFlag result = KeepGitDirFlag.Parse(scenario.Text, scenario.EscapeChar);
            Assert.Equal(scenario.Text, result.ToString());
            Assert.Collection(result.Tokens, scenario.TokenValidators);
            scenario.Validate?.Invoke(result);
        }
        else
        {
            ParseException exception = Assert.Throws<ParseException>(
                () => KeepGitDirFlag.Parse(scenario.Text, scenario.EscapeChar));
            Assert.Equal(scenario.ParseExceptionPosition.Line, exception.Position.Line);
            Assert.Equal(scenario.ParseExceptionPosition.Column, exception.Position.Column);
        }
    }

    [Theory]
    [MemberData(nameof(CreateTestInput))]
    public void Create(CreateTestScenario scenario)
    {
        KeepGitDirFlag result = new();
        Assert.Collection(result.Tokens, scenario.TokenValidators);
        scenario.Validate?.Invoke(result);
    }

    public static IEnumerable<object[]> ParseTestInput()
    {
        KeepGitDirFlagParseTestScenario[] testInputs = new KeepGitDirFlagParseTestScenario[]
        {
            new KeepGitDirFlagParseTestScenario
            {
                Text = "--keep-git-dir",
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "keep-git-dir")
                },
                Validate = result =>
                {
                    Assert.Equal("--keep-git-dir", result.ToString());
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
                TokenValidators = new Action<Token>[]
                {
                    token => ValidateSymbol(token, '-'),
                    token => ValidateSymbol(token, '-'),
                    token => ValidateKeyword(token, "keep-git-dir")
                },
                Validate = result =>
                {
                    Assert.Equal("--keep-git-dir", result.ToString());
                }
            },
        };

        return testInputs.Select(input => new object[] { input });
    }

    public class KeepGitDirFlagParseTestScenario : ParseTestScenario<KeepGitDirFlag>
    {
        public char EscapeChar { get; set; }
    }

    public class CreateTestScenario : TestScenario<KeepGitDirFlag>
    {
    }
}
