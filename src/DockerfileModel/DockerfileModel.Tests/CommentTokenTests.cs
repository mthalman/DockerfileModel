using System;
using DockerfileModel.Tokens;
using Xunit;

namespace DockerfileModel.Tests
{
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

        [Fact]
        public void Create()
        {
            CommentToken comment = CommentToken.Create("test");
            Assert.Equal("#test", comment.ToString());

            comment = CommentToken.Create(" \ttest");
            Assert.Equal("# \ttest", comment.ToString());
        }

        [Fact]
        public void Text()
        {
            CommentToken comment = CommentToken.Create("test");

            Assert.Equal("test", comment.Text);
            Assert.Equal("test", comment.TextToken.Value);

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
}
