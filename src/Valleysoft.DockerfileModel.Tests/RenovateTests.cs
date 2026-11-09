using System.Text.Json;
using Valleysoft.DockerfileModel.DiffTest;

namespace Valleysoft.DockerfileModel.Tests;

public class RenovateTests
{
    [Fact]
    public void FsCheckPackages_AreGroupedForAllUpdateTypes()
    {
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(UpstreamCorpus.ResolveDefaultCompatibilityPath())!,
                ".github", "renovate.json")));
        JsonElement rule = Assert.Single(config.RootElement.GetProperty("packageRules").EnumerateArray(),
            rule => rule.TryGetProperty("matchPackageNames", out JsonElement packages) &&
                packages.EnumerateArray().Any(value => value.GetString() == "FsCheck"));

        Assert.Equal("nuget", Assert.Single(rule.GetProperty("matchManagers").EnumerateArray()).GetString());
        Assert.Equal(new[] { "FsCheck", "FsCheck.Xunit" },
            rule.GetProperty("matchPackageNames").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("FsCheck packages", rule.GetProperty("groupName").GetString());
        Assert.Equal("fscheck", rule.GetProperty("groupSlug").GetString());
        Assert.False(rule.TryGetProperty("matchUpdateTypes", out _));
    }
}
