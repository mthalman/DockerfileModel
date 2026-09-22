using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class CommentTokenTests
{
    [Fact]
    public void Parse()
    {
        CommentToken comment = CommentToken.Parse("#test");
        Assert.Equal("#test", comment.ToString());

        comment = CommentToken.Parse("# \ttest");
        Assert.Equal("# \ttest", comment.ToString());
    }

    [Theory]
    [InlineData("\n", "")]
    [InlineData("\r\n", "")]
    [InlineData("\n", " \t")]
    [InlineData("\r\n", " \t")]
    public void EmptyCommentDoesNotConsumeTheFollowingLine(string newline, string whitespace)
    {
        var result = CommentToken.GetParser()(new Input($"#{whitespace}{newline}80"));
        Assert.True(result.WasSuccessful);
        Assert.Equal($"#{whitespace}", string.Concat(result.Value.Select(token => token.ToString())));
        Assert.Equal(newline + "80", result.Remainder.Source.Substring(result.Remainder.Position));
    }

    [Fact]
    public void Create()
    {
        CommentToken comment = new("test");
        Assert.Equal("#test", comment.ToString());

        comment = new CommentToken(" \ttest");
        Assert.Equal("# \ttest", comment.ToString());
    }

    [Fact]
    public void Text()
    {
        CommentToken comment = new("test");

        Assert.Equal("test", comment.Text);
        Assert.Equal("test", Assert.IsType<StringToken>(comment.TextToken).Value);

        comment.Text = "foo";
        Assert.Equal("foo", comment.Text);
        Assert.Equal("foo", comment.TextToken.Value);

        comment.TextToken.Value = "foo2";
        Assert.Equal("foo2", comment.Text);
        Assert.Equal("foo2", comment.TextToken.Value);

        comment.TextToken = new StringToken("foo3");
        Assert.Equal("foo3", comment.Text);
        Assert.Equal("foo3", comment.TextToken.Value);

        comment.Text = null;
        Assert.Null(comment.Text);
        Assert.Null(comment.TextToken);
        Assert.Equal("#", comment.ToString());

        comment.Text = "foo";

        comment.Text = "";
        Assert.Null(comment.Text);
        Assert.Null(comment.TextToken);
        Assert.Equal("#", comment.ToString());

        comment.Text = "foo";

        comment.TextToken = null;
        Assert.Null(comment.Text);
        Assert.Null(comment.TextToken);
        Assert.Equal("#", comment.ToString());
    }
}
