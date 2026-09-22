using Valleysoft.DockerfileModel.Parsing;

namespace Valleysoft.DockerfileModel.Tests;

public class ParserEngineMigrationTests
{
    [Fact]
    public void CharacterParserUsesSuperpowerForSuccessfulMatches()
    {
        IInput input = new Input("abc");

        IResult<char> result = Parse.Char('a')(input);

        Assert.True(result.WasSuccessful);
        Assert.Equal('a', result.Value);
        Assert.Equal(1, result.Remainder.Position);
        Assert.Equal(1, result.Remainder.Line);
        Assert.Equal(2, result.Remainder.Column);
    }

    [Fact]
    public void CharacterParserPreservesCompatibilityFailureContract()
    {
        IInput input = new Input("xyz");

        IResult<char> result = Parse.Char('a')(input);

        Assert.False(result.WasSuccessful);
        Assert.Same(input, result.Remainder);
        Assert.Equal("unexpected 'x'", result.Message);
        Assert.Equal(new[] { "a" }, result.Expectations);
    }

    [Fact]
    public void CharacterParserPreservesLineAndColumnAfterNewline()
    {
        IInput input = new Input("a\nb").Advance().Advance();

        IResult<char> result = Parse.Char('b')(input);

        Assert.True(result.WasSuccessful);
        Assert.Equal(2, result.Remainder.Line);
        Assert.Equal(2, result.Remainder.Column);
    }

    [Fact]
    public void SuperpowerFailureDoesNotReplaceCompatibilityInputPosition()
    {
        IInput input = new Input("a");
        IResult<char> result = Parse.Char(_ => false, "custom character")(input);

        Assert.False(result.WasSuccessful);
        Assert.Same(input, result.Remainder);
        Assert.Equal(0, result.Remainder.Position);
        Assert.Equal(1, result.Remainder.Line);
        Assert.Equal(1, result.Remainder.Column);
    }
}
