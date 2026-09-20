using System.Text.Json;
using System.Text.RegularExpressions;
using Valleysoft.DockerfileModel.DiffTest;

namespace Valleysoft.DockerfileModel.Tests;

public class UpstreamRenovateTests
{
    [Theory]
    [InlineData("dockerfile/1.27.0", "1.27.0")]
    [InlineData("dockerfile/1.27.1", "1.27.1")]
    [InlineData("dockerfile/2.0.0", "2.0.0")]
    [InlineData("dockerfile/1.28.0-labs", null)]
    [InlineData("dockerfile/1.28.0-rc1", null)]
    [InlineData("dockerfile/1.28.0-rc1-labs", null)]
    [InlineData("v0.33.0", null)]
    [InlineData("dockerfile/1", null)]
    [InlineData("master", null)]
    public void ReleaseFilter_SelectsOnlyFullStableFrontendVersions(string tag, string? expected)
    {
        using JsonDocument config = LoadConfig();
        JsonElement manager = Assert.Single(config.RootElement.GetProperty("customManagers").EnumerateArray());
        Match match = Regex.Match(tag, manager.GetProperty("extractVersionTemplate").GetString()!);

        Assert.Equal(expected, match.Success ? match.Groups["version"].Value : null);
    }

    [Fact]
    public void Manager_ExtractsOneVersionAndRequiresMaintainerReview()
    {
        using JsonDocument config = LoadConfig();
        JsonElement manager = Assert.Single(config.RootElement.GetProperty("customManagers").EnumerateArray());
        string pattern = Assert.Single(manager.GetProperty("matchStrings").EnumerateArray()).GetString()!;
        Match match = Assert.Single(Regex.Matches(
            File.ReadAllText(UpstreamCorpus.ResolveDefaultCompatibilityPath()), pattern).Cast<Match>());
        Assert.Matches(@"^\d+\.\d+\.\d+$", match.Groups["currentValue"].Value);
        Assert.Equal("github-releases", manager.GetProperty("datasourceTemplate").GetString());
        Assert.Equal("moby/buildkit", manager.GetProperty("packageNameTemplate").GetString());
        JsonElement rule = Assert.Single(config.RootElement.GetProperty("packageRules").EnumerateArray(),
            rule => rule.TryGetProperty("matchManagers", out JsonElement managers) &&
                managers.EnumerateArray().Any(value => value.GetString() == "custom.regex"));
        Assert.False(rule.GetProperty("automerge").GetBoolean());
    }

    private static JsonDocument LoadConfig() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(Path.GetDirectoryName(UpstreamCorpus.ResolveDefaultCompatibilityPath())!,
            ".github", "renovate.json")));
}
