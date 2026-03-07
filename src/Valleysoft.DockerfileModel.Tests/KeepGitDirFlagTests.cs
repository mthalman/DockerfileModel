using Valleysoft.DockerfileModel.Tokens;

using static Valleysoft.DockerfileModel.Tests.TokenValidator;

namespace Valleysoft.DockerfileModel.Tests;

public class KeepGitDirFlagTests
{
    [Theory]
    [MemberData(nameof(ParseTestInput))]
    public void Parse(ParseTestScenario<KeepGitDirFlag> scenario) =>
        TestHelper.RunParseTest(scenario, KeepGitDirFlag.Parse);

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
        ParseTestScenario<KeepGitDirFlag>[] testInputs = new ParseTestScenario<KeepGitDirFlag>[]
        {
            new ParseTestScenario<KeepGitDirFlag>
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

    public class CreateTestScenario : TestScenario<KeepGitDirFlag>
    {
    }
}
