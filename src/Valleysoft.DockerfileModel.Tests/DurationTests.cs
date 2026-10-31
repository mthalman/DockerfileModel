using System.Globalization;

namespace Valleysoft.DockerfileModel.Tests;

public class DurationTests
{
    [Theory]
    [InlineData("1s", "00:00:01")]
    [InlineData("2m", "00:02:00")]
    [InlineData("3h", "03:00:00")]
    [InlineData("1ms", "00:00:00.001")]
    [InlineData("250ms", "00:00:00.250")]
    [InlineData("1.5ms", "00:00:00.0015000")]
    [InlineData("0.5ms", "00:00:00.0005000")]
    [InlineData("0.0001ms", "00:00:00.0000001")]
    [InlineData("0.0003ms", "00:00:00.0000003")]
    [InlineData("999.9999ms", "00:00:00.9999999")]
    [InlineData("1000.0001ms", "00:00:01.0000001", "1s0.0001ms")]
    [InlineData("1h2m3s", "01:02:03")]
    [InlineData("1h2m3s4ms", "01:02:03.004")]
    [InlineData("1h2m3s4.5678ms", "01:02:03.0045678")]
    [InlineData("2m4ms", "00:02:00.004")]
    [InlineData("3s4ms", "00:00:03.004")]
    [InlineData("1.5h", "01:30:00", "1h30m")]
    [InlineData("1.5m", "00:01:30", "1m30s")]
    [InlineData("1.5s", "00:00:01.500", "1s500ms")]
    [InlineData("1.5h2.5m3.5s4ms", "01:32:33.504", "1h32m33s504ms")]
    [InlineData("61s", "00:01:01", "1m1s")]
    [InlineData("34h", "1.10:00:00")]
    [InlineData("", "00:00:00")]
    [InlineData("0ms", "00:00:00", "")]
    public void Parse(string text, string expectedTimeSpanString, string? expected = null)
    {
        TimeSpan expectedTimeSpan = TimeSpan.Parse(expectedTimeSpanString, CultureInfo.InvariantCulture);

        Duration result = Duration.Parse(text);
        Assert.Equal(expectedTimeSpan, result.TimeSpan);

        if (expected is null)
        {
            expected = text;
        }
        Assert.Equal(expected, result.ToString());

        result = new Duration(expectedTimeSpan);
        Assert.Equal(expected, result.ToString());
        Assert.Equal(expectedTimeSpan, Duration.Parse(result.ToString()).TimeSpan);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    public async Task ParseUsesInvariantCulture(string cultureName)
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        await Task.Run(() =>
        {
            CultureInfo taskCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

                Parse("1.5h", "01:30:00", "1h30m");
                Parse("1.5m", "00:01:30", "1m30s");
                Parse("1.5s", "00:00:01.500", "1s500ms");
                Parse("1ms", "00:00:00.001");
                Parse("1.5ms", "00:00:00.0015000");
                Parse("0.0003ms", "00:00:00.0000003");
                Parse("1.5h2.5m3.5s4ms", "01:32:33.504", "1h32m33s504ms");
            }
            finally
            {
                CultureInfo.CurrentCulture = taskCulture;
            }
        });
        Assert.Equal(originalCulture, CultureInfo.CurrentCulture);
    }

    [Theory]
    [InlineData("1msjunk")]
    [InlineData("1mss")]
    [InlineData("1hgarbage")]
    [InlineData("1h2m3s4ms!")]
    [InlineData("1s2m")]
    [InlineData("1h1h")]
    [InlineData("1")]
    [InlineData("1,5h")]
    [InlineData("1ns")]
    [InlineData("1us")]
    [InlineData("-1s")]
    [InlineData("1s ")]
    public void ParseRejectsInvalidSuffixesAndUnsupportedSyntax(string text)
    {
        Assert.Throws<ParseException>(() => Duration.Parse(text));
    }

    [Theory]
    [InlineData("1..5h")]
    [InlineData("1..5m")]
    [InlineData("1..5s")]
    [InlineData("1..5ms")]
    public void ParseRejectsMalformedNumbersWithFormatException(string text)
    {
        Assert.Throws<FormatException>(() => Duration.Parse(text));
    }
}
