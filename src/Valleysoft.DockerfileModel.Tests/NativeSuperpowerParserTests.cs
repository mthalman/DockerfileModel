namespace Valleysoft.DockerfileModel.Tests;

public class NativeSuperpowerParserTests
{
    [Fact]
    public void CharacterParserReturnsNativeSuccessfulResult()
    {
        TextSpan input = new("abc");

        Result<char> result = Character.EqualTo('a')(input);

        Assert.True(result.HasValue);
        Assert.Equal('a', result.Value);
        Assert.Equal(1, result.Remainder.Position.Absolute);
        Assert.Equal(1, result.Remainder.Position.Line);
        Assert.Equal(2, result.Remainder.Position.Column);
    }

    [Fact]
    public void CharacterParserReturnsNativeFailureResult()
    {
        TextSpan input = new("xyz");

        Result<char> result = Character.EqualTo('a')(input);

        Assert.False(result.HasValue);
        Assert.Equal(input.Position, result.ErrorPosition);
        Assert.Equal(new[] { "`a`" }, result.Expectations);
    }

    [Fact]
    public void CharacterParserTracksLineAndColumnAfterNewline()
    {
        TextSpan input = new TextSpan("a\nb").Skip(2);

        Result<char> result = Character.EqualTo('b')(input);

        Assert.True(result.HasValue);
        Assert.Equal(2, result.Remainder.Position.Line);
        Assert.Equal(2, result.Remainder.Position.Column);
    }

    [Fact]
    public void MatchingParserReportsNativeFailurePosition()
    {
        TextSpan input = new("a");
        Result<char> result = Character.Matching(_ => false, "custom character")(input);

        Assert.False(result.HasValue);
        Assert.Equal(0, result.ErrorPosition.Absolute);
        Assert.Equal(1, result.ErrorPosition.Line);
        Assert.Equal(1, result.ErrorPosition.Column);
    }
}
