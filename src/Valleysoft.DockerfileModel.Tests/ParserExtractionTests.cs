using Valleysoft.DockerfileModel.Parsing;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class ParserExtractionTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void HeredocParserPreservesNativeRemainder(string newLine)
    {
        string prefix = $"# prefix{newLine}";
        string heredoc = $"<<EOF{newLine}body{newLine}EOF{newLine}";
        string suffix = $"FROM scratch{newLine}";
        TextSpan input = new TextSpan(prefix + heredoc + suffix).Skip(prefix.Length);

        Result<IEnumerable<Token>> result = HeredocParsers.HeredocTokenParser()(input);

        Assert.True(result.HasValue);
        Assert.Equal(heredoc, string.Concat(result.Value));
        Assert.Equal(prefix.Length + heredoc.Length, result.Remainder.Position.Absolute);
        Assert.Equal(5, result.Remainder.Position.Line);
        Assert.Equal(1, result.Remainder.Position.Column);
        Assert.Equal('F', result.Remainder[0]);

        TextSpan advanced = result.Remainder.Skip(1);
        Assert.Equal(result.Remainder.Position.Absolute + 1, advanced.Position.Absolute);
        Assert.Equal(5, advanced.Position.Line);
        Assert.Equal(2, advanced.Position.Column);
        Assert.Equal(suffix, FromInstruction.GetParser()(result.Remainder).Value.ToString());
    }

    [Theory]
    [InlineData("<<EOF", 1, 6)]
    [InlineData("<<EOF\nbody", 2, 5)]
    [InlineData("<<EOF\nbody\nEOF", 3, 4)]
    public void HeredocParserPreservesEndOfInput(string text, int line, int column)
    {
        TextSpan input = new(text);

        Result<IEnumerable<Token>> result = HeredocParsers.HeredocTokenParser()(input);

        Assert.True(result.HasValue);
        Assert.Equal(text, string.Concat(result.Value));
        Assert.Equal(text.Length, result.Remainder.Position.Absolute);
        Assert.Equal(line, result.Remainder.Position.Line);
        Assert.Equal(column, result.Remainder.Position.Column);
        Assert.True(result.Remainder.IsAtEnd);
    }

    [Theory]
    [InlineData("<EOF", "heredoc marker <<")]
    [InlineData("<<", "valid heredoc marker")]
    public void HeredocParserFailureReportsNativePosition(string text, string expectation)
    {
        TextSpan input = new(text);

        Result<IEnumerable<Token>> result = HeredocParsers.HeredocTokenParser()(input);

        Assert.False(result.HasValue);
        Assert.Equal(input.Position, result.ErrorPosition);
        Assert.Equal(expectation, result.ErrorMessage);
    }
}
