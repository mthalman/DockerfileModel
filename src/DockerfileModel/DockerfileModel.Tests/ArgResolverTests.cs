using System.Collections.Generic;
using Xunit;

namespace DockerfileModel.Tests
{
    public class ArgResolverTests
    {
        [Theory]
        [InlineData("alpine:prefix-$TAG", '\\', "TAG", "test", null, null, "alpine:prefix-test")]
        [InlineData("alpine:$TAGx", '\\', "TAG", "test", null, null, "alpine:")]
        [InlineData("alpine:$TAG-x", '\\', "TAG", "test", null, null, "alpine:test-x")]
        [InlineData("alpine:$TAG-suffix", '\\', "TAG", "test", null, null, "alpine:test-suffix")]
        [InlineData("$image", '\\', "image", "test", null, null, "test")]
        [InlineData("$image:$tag", '\\', "image", "test", "tag", "foo", "test:foo")]
        [InlineData("alpine:`$TAG", '`', "TAG", "test", null, null, "alpine:`$TAG")]
        [InlineData("$image$tag", '`', "image", "test", "tag", ":foo", "test:foo")]
        public void Resolve(
            string text, char escapeChar, string arg1Name, string arg1Value, string arg2Name, string arg2Value, string expected)
        {
            Dictionary<string, string> args = new Dictionary<string, string>
            {
                { arg1Name, arg1Value }
            };
            if (arg2Name != null)
            {
                args.Add(arg2Name, arg2Value);
            }

            string actual = ArgResolver.Resolve(text, args, escapeChar);
            Assert.Equal(expected, actual);
        }
    }
}
