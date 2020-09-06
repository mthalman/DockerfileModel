using System;
using System.Linq;
using Xunit;

namespace DockerfileModel.Tests
{
    public static class LineValidator
    {
        public static void ValidateLine<T>(DockerfileLine line, string text, params Action<Token>[] tokenValidators)
        {
            Assert.IsType<T>(line);
            Assert.Equal(text, line.ToString());

            if (tokenValidators.Any())
            {
                Assert.Collection(line.Tokens, tokenValidators);
            }
        }
    }
}
