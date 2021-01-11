using System;
using Xunit;

namespace Valleysoft.DockerfileModel.Tests
{
    public class DurationTests
    {
        [Theory]
        [InlineData("1s", "00:00:01")]
        [InlineData("2m", "00:02:00")]
        [InlineData("3h", "03:00:00")]
        [InlineData("1h2m3s", "01:02:03")]
        [InlineData("1.5h", "01:30:00", "1h30m")]
        [InlineData("61s", "00:01:01", "1m1s")]
        [InlineData("34h", "1.10:00:00")]
        public void Parse(string text, string expectedTimeSpanString, string expected = null)
        {
            TimeSpan expectedTimeSpan = TimeSpan.Parse(expectedTimeSpanString);

            Duration result = Duration.Parse(text);
            Assert.Equal(expectedTimeSpan, result.TimeSpan);

            if (expected is null)
            {
                expected = text;
            }
            Assert.Equal(expected, result.ToString());

            result = new Duration(expectedTimeSpan);
            Assert.Equal(expected, result.ToString());
        }
    }
}
