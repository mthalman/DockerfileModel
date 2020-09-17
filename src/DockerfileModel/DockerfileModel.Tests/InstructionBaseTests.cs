using System.Collections.Generic;
using Xunit;

namespace DockerfileModel.Tests
{
    public class InstructionBaseTests
    {
        [Theory]
        [InlineData("alpine:prefix-$TAG", '\\', "TAG", "test", null, null, "alpine:prefix-test")]
        [InlineData("alpine:$TAGx", '\\', "TAG", "test", null, null, "alpine:$TAGx")]
        [InlineData("alpine:$TAG-x", '\\', "TAG", "test", null, null, "alpine:test-x")]
        [InlineData("alpine:$TAG-suffix", '\\', "TAG", "test", null, null, "alpine:test-suffix")]
        [InlineData("$image", '\\', "image", "test", null, null, "test")]
        [InlineData("$image:$tag", '\\', "image", "test", "tag", "foo", "test:foo")]
        [InlineData("alpine:`$TAG", '`', "TAG", "test", null, null, "alpine:`$TAG")]
        public void ResolveArgValues(
            string imageName, char escapeChar, string arg1Name, string arg1Value, string arg2Name, string arg2Value, string expectedImageName)
        {
            Dictionary<string, string> args = new Dictionary<string, string>
            {
                { arg1Name, arg1Value }
            };
            if (arg2Name != null)
            {
                args.Add(arg2Name, arg2Value);
            }

            FromInstruction line = FromInstruction.Create(imageName);
            line.ResolveArgValues(args, escapeChar);
            Assert.Equal(expectedImageName, line.ImageName);
        }
    }
}
