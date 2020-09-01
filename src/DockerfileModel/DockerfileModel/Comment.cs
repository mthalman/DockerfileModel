using System;
using System.Collections.Generic;
using System.Linq;
using Sprache;

namespace DockerfileModel
{
    public class Comment : IDockerfileLine
    {
        private Comment(string text)
        {
            Tokens = DockerfileParser.FilterNulls(DockerfileParser.CommentText().Parse(text));
        }

        public IEnumerable<Token> Tokens { get; }

        public LiteralToken Text => Tokens.OfType<LiteralToken>().First();

        public LineType Type => LineType.Comment;

        public static Comment Create(string comment) =>
            new Comment($"# {comment}");

        public static Comment CreateFromRawText(string text) =>
            new Comment(text);

        public override string ToString() =>
            String.Join("", Tokens
                .Select(token => token.Value)
                .ToArray());
    }
}
