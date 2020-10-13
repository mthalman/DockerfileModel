using System;
using System.Collections.Generic;
using Xunit;

namespace DockerfileModel.Tests
{
    public class AggregateTokenTests
    {
        [Theory]
        [InlineData("$", '\\', null, null, null, null, "$")]
        [InlineData("\"$\"", '\\', null, null, null, null, "\"$\"")]
        [InlineData("$test", '\\', null, null, null, null, "")]
        [InlineData("x$test", '\\', null, null, null, null, "x")]
        [InlineData("$}", '\\', null, null, null, null, "$}")]
        [InlineData("${}", '\\', null, null, null, null, null, "Bad substitution")]
        [InlineData("${", '\\', null, null, null, null, null, "Missing '}'")]
        [InlineData("\\${ab}", '\\', null, null, null, null, "\\${ab}")]
        [InlineData("$test\"x\"", '\\', null, null, null, null, "\"x\"")]
        [InlineData("alpine:prefix-$TAG", '\\', "TAG", "test", null, null, "alpine:prefix-test")]
        [InlineData("alpine:$TAGx", '\\', "TAG", "test", null, null, "alpine:")]
        [InlineData("alpine:$TAG-x", '\\', "TAG", "test", null, null, "alpine:test-x")]
        [InlineData("alpine:$TAG-suffix", '\\', "TAG", "test", null, null, "alpine:test-suffix")]
        [InlineData("$image", '\\', "image", "test", null, null, "test")]
        [InlineData("$image:$tag", '\\', "image", "test", "tag", "foo", "test:foo")]
        [InlineData("alpine:`$TAG", '`', "TAG", "test", null, null, "alpine:`$TAG")]
        [InlineData("$image$tag", '`', "image", "test", "tag", ":foo", "test:foo")]
        [InlineData("alpine${TAG}x", '\\', "TAG", ":test", null, null, "alpine:testx")]
        [InlineData("alpine${TA`$`{`}G}x", '`', "TA`$`{`}G", ":test", null, null, "alpine:testx")]
        [InlineData("alpine:${TAG}x", '\\', "TAG", "test", null, null, "alpine:testx")]
        [InlineData("alpine:\\${TAG}", '\\', "TAG", "test", null, null, "alpine:${TAG}")]
        [InlineData("repo:${TAG-test}", '\\', null, null, null, null, "repo:test")]
        [InlineData("repo:${TAG-test}", '\\', "TAG", null, null, null, "repo:")]
        [InlineData("repo:${TAG-test}", '\\', "TAG", "foo", null, null, "repo:foo")]
        [InlineData("repo:${TAG:-test}", '\\', null, null, null, null, "repo:test")]
        [InlineData("repo:${TAG:-test}", '\\', "TAG", null, null, null, "repo:test")]
        [InlineData("repo:${TAG:-test}", '\\', "TAG", "foo", null, null, "repo:foo")]
        [InlineData("repo:${TAG+test}", '\\', null, null, null, null, "repo:")]
        [InlineData("repo:${TAG+test}", '\\', "TAG", null, null, null, "repo:test")]
        [InlineData("repo:${TAG+test}", '\\', "TAG", "foo", null, null, "repo:test")]
        [InlineData("repo:${TAG:+test}", '\\', null, null, null, null, "repo:")]
        [InlineData("repo:${TAG:+test}", '\\', "TAG", null, null, null, "repo:")]
        [InlineData("repo:${TAG:+test}", '\\', "TAG", "foo", null, null, "repo:foo")]
        [InlineData("repo:${TAG?err}", '\\', null, null, null, null, null, "err")]
        [InlineData("repo:${TAG?err}", '\\', "TAG", null, null, null, "repo:")]
        [InlineData("repo:${TAG?err}", '\\', "TAG", "foo", null, null, "repo:foo")]
        [InlineData("repo:${TAG:?err}", '\\', null, null, null, null, null, "err")]
        [InlineData("repo:${TAG:?err}", '\\', "TAG", null, null, null, null, "err")]
        [InlineData("repo:${TAG:?err}", '\\', "TAG", "foo", null, null, "repo:foo")]
        [InlineData("repo:${TAG:-${TAG2}}", '\\', "TAG2", "foo", null, null, "repo:foo")]
        [InlineData("repo:\\${TAG:-${TAG2}}", '\\', "TAG2", "foo", null, null, "repo:${TAG:-foo}")]
        [InlineData("repo:${TAG:-${TAG2}}", '\\', "TAG", "test", "TAG2", "foo", "repo:test")]
        [InlineData("repo:${TAG:-${TAG2:-a${TAG3}b}}", '\\', "TAG3", "foo", null, null, "repo:afoob")]
        public void Resolve(
            string text, char escapeChar, string arg1Name, string arg1Value, string arg2Name, string arg2Value, string expected, string expectedError = null)
        {
            Dictionary<string, string> args = new Dictionary<string, string>();
            if (arg1Name != null)
            {
                args.Add(arg1Name, arg1Value);
            };

            if (arg2Name != null)
            {
                args.Add(arg2Name, arg2Value);
            }

            FromInstruction inst = FromInstruction.Parse($"FROM {text}", escapeChar);

            if (expectedError is null)
            {
                string actual = inst.ResolveVariables(args);
                Assert.Equal($"FROM {expected}", actual);
            }
            else
            {
                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => inst.ResolveVariables(args));
                Assert.Equal(expectedError, ex.Message);
            }
        }
    }
}
