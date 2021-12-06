using Xunit;

namespace Valleysoft.DockerfileModel.Tests;

public class AggregateTokenTests
{
    [Theory]
    [InlineData("$", '\\', null, null, null, null, "$")]
    [InlineData("\"$\"", '\\', null, null, null, null, "\"$\"")]
    [InlineData("$test", '\\', null, null, null, null, "")]
    [InlineData("x$test", '\\', null, null, null, null, "x")]
    [InlineData("$}", '\\', null, null, null, null, "$}")]
    [InlineData("\\${ab}", '\\', null, null, null, null, "\\${ab}")]
    [InlineData("$test\"x\"", '\\', null, null, null, null, "\"x\"")]
    [InlineData("alpine:prefix-$TAG", '\\', "TAG", "test", null, null, "alpine:prefix-test")]
    [InlineData("alpine:$TAGx", '\\', "TAG", "test", null, null, "alpine:")]
    [InlineData("alpine:$TAG-x", '\\', "TAG", "test", null, null, "alpine:test-x")]
    [InlineData("alpine:$TAG-suffix", '\\', "TAG", "test", null, null, "alpine:test-suffix")]
    [InlineData("$image", '\\', "image", "test", null, null, "test")]
    [InlineData("${image}", '\\', "image", "test", null, null, "test")]
    [InlineData("${image}}", '\\', "image", "test", null, null, "test}")]
    [InlineData("$image:$tag", '\\', "image", "test", "tag", "foo", "test:foo")]
    [InlineData("$image:$tag-\\\\a", '\\', "image", "test", "tag", "foo", "test:foo-\\a", true)]
    [InlineData("alpine:`$TAG", '`', "TAG", "test", null, null, "alpine:`$TAG")]
    [InlineData("$image$tag", '`', "image", "test", "tag", ":foo", "test:foo")]
    [InlineData("alpine${TAG}x", '\\', "TAG", ":test", null, null, "alpine:testx")]
    [InlineData("alpine:${TAG}x", '\\', "TAG", "test", null, null, "alpine:testx")]
    [InlineData("alpine:\\${TAG}", '\\', "TAG", "test", null, null, "alpine:\\${TAG}")]
    [InlineData("repo:${TAG-test}", '\\', null, null, null, null, "repo:test")]
    [InlineData("repo:${TAG-test}", '\\', "TAG", null, null, null, "repo:")]
    [InlineData("repo:${TAG-test}", '\\', "TAG", "foo", null, null, "repo:foo")]
    [InlineData("repo:${TAG:-test}", '\\', null, null, null, null, "repo:test")]
    [InlineData("repo:${TAG:-test}", '\\', "TAG", null, null, null, "repo:test")]
    [InlineData("repo:${TAG:-test}", '\\', "TAG", "foo", null, null, "repo:foo")]
    [InlineData("repo:${TAG:-te:-st}", '\\', null, null, null, null, "repo:te:-st")]
    [InlineData("repo:${TAG+test}", '\\', null, null, null, null, "repo:")]
    [InlineData("repo:${TAG+test}", '\\', "TAG", null, null, null, "repo:test")]
    [InlineData("repo:${TAG+test}", '\\', "TAG", "foo", null, null, "repo:test")]
    [InlineData("repo:${TAG+te:+st}", '\\', "TAG", null, null, null, "repo:te:+st")]
    [InlineData("repo:${TAG:+test}", '\\', null, null, null, null, "repo:")]
    [InlineData("repo:${TAG:+test}", '\\', "TAG", null, null, null, "repo:")]
    [InlineData("repo:${TAG:+test}", '\\', "TAG", "foo", null, null, "repo:test")]
    [InlineData("repo:${TAG?err}", '\\', null, null, null, null, null, false, "err")]
    [InlineData("repo:${TAG?err}", '\\', "TAG", null, null, null, "repo:")]
    [InlineData("repo:${TAG?err}", '\\', "TAG", "foo", null, null, "repo:foo")]
    [InlineData("repo:${TAG:?err}", '\\', null, null, null, null, null, false, "err")]
    [InlineData("repo:${TAG:?err}", '\\', "TAG", null, null, null, null, false, "err")]
    [InlineData("repo:${TAG:?err}", '\\', "TAG", "foo", null, null, "repo:foo")]
    [InlineData("repo:${TAG:-${TAG2}}", '\\', "TAG2", "foo", null, null, "repo:foo")]
    [InlineData("repo:\\${TAG:-${TAG2}}", '\\', "TAG2", "foo", null, null, "repo:\\${TAG:-foo}")]
    [InlineData("repo:\\${TAG:-${TAG2}}", '\\', "TAG2", "foo", null, null, "repo:${TAG:-foo}", true)]
    [InlineData("repo:${TAG:-${TAG2}}", '\\', "TAG", "test", "TAG2", "foo", "repo:test")]
    [InlineData("repo:${TAG:-${TAG2:-a${TAG3}b}}", '\\', "TAG3", "foo", null, null, "repo:afoob")]
    public void Resolve(
        string text, char escapeChar, string arg1Name, string arg1Value, string arg2Name, string arg2Value, string expected,
        bool removeEscapeCharacters = false, string expectedError = null)
    {
        Dictionary<string, string> args = new();
        if (arg1Name != null)
        {
            args.Add(arg1Name, arg1Value);
        };

        if (arg2Name != null)
        {
            args.Add(arg2Name, arg2Value);
        }

        ResolutionOptions options = new()
        {
            RemoveEscapeCharacters = removeEscapeCharacters
        };

        FromInstruction inst = FromInstruction.Parse($"FROM {text}", escapeChar);

        if (expectedError is null)
        {
            string actual = inst.ResolveVariables(escapeChar, args, options);
            Assert.Equal($"FROM {expected}", actual);
        }
        else
        {
            VariableSubstitutionException ex = Assert.Throws<VariableSubstitutionException>(() => inst.ResolveVariables(escapeChar, args, options));
            Assert.Equal($"Variable 'TAG' is not set. Error detail: '{expectedError}'.", ex.Message);
        }
    }
}
