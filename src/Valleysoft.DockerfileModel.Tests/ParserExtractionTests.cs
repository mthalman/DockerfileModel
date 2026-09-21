using Sprache;
using Valleysoft.DockerfileModel.Parsing;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class ParserExtractionTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void HeredocParserPreservesRemainderAndMemos(string newLine)
    {
        string prefix = $"# prefix{newLine}";
        string heredoc = $"<<EOF{newLine}body{newLine}EOF{newLine}";
        string suffix = $"FROM scratch{newLine}";
        IInput input = new Input(prefix + heredoc + suffix);
        for (int i = 0; i < prefix.Length; i++)
        {
            input = input.Advance();
        }
        object memoKey = new();
        object memoValue = new();
        input.Memos.Add(memoKey, memoValue);

        IResult<IEnumerable<Token>> result = HeredocParsers.HeredocTokenParser()(input);

        Assert.True(result.WasSuccessful);
        Assert.Equal(heredoc, string.Concat(result.Value));
        Assert.Equal(prefix.Length + heredoc.Length, result.Remainder.Position);
        Assert.Equal(5, result.Remainder.Line);
        Assert.Equal(1, result.Remainder.Column);
        Assert.Same(input.Memos, result.Remainder.Memos);
        Assert.Same(memoValue, result.Remainder.Memos[memoKey]);
        Assert.Equal('F', result.Remainder.Current);

        IInput advanced = result.Remainder.Advance();
        Assert.Equal(result.Remainder.Position + 1, advanced.Position);
        Assert.Equal(5, advanced.Line);
        Assert.Equal(2, advanced.Column);
        Assert.Same(input.Memos, advanced.Memos);
        Assert.Equal(suffix, FromInstruction.GetParser()(result.Remainder).Value.ToString());
    }

    [Theory]
    [InlineData("<<EOF", 1, 6)]
    [InlineData("<<EOF\nbody", 2, 5)]
    [InlineData("<<EOF\nbody\nEOF", 3, 4)]
    public void HeredocParserPreservesEndOfInput(string text, int line, int column)
    {
        IInput input = new Input(text);

        IResult<IEnumerable<Token>> result = HeredocParsers.HeredocTokenParser()(input);

        Assert.True(result.WasSuccessful);
        Assert.Equal(text, string.Concat(result.Value));
        Assert.Equal(text.Length, result.Remainder.Position);
        Assert.Equal(line, result.Remainder.Line);
        Assert.Equal(column, result.Remainder.Column);
        Assert.True(result.Remainder.AtEnd);
        Assert.Same(input.Memos, result.Remainder.Memos);
        Assert.Throws<InvalidOperationException>(() => result.Remainder.Advance());
    }

    [Theory]
    [InlineData("<EOF", "Expected heredoc marker <<")]
    [InlineData("<<", "Invalid heredoc marker")]
    public void HeredocParserFailureDoesNotAdvance(string text, string message)
    {
        IInput input = new Input(text);

        IResult<IEnumerable<Token>> result = HeredocParsers.HeredocTokenParser()(input);

        Assert.False(result.WasSuccessful);
        Assert.Same(input, result.Remainder);
        Assert.Equal(message, result.Message);
        Assert.Empty(result.Expectations);
    }
}
