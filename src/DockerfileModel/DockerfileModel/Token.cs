namespace DockerfileModel
{
    public abstract class Token
    {
        public Token(string value)
        {
            this.Value = value;
        }

        public string Value { get; set; }
    }

    public class KeywordToken : Token
    {
        public KeywordToken(string value) : base(value)
        {
        }
    }

    public class OperatorToken : Token
    {
        public OperatorToken(string value) : base(value)
        {
        }
    }

    public class LiteralToken : Token
    {
        public LiteralToken(string value) : base(value)
        {
        }
    }

    public class WhitespaceToken : Token
    {
        public WhitespaceToken(string value) : base(value)
        {
        }
    }

    public class NewLineToken : WhitespaceToken
    {
        public NewLineToken(string value) : base(value)
        {
        }
    }

    public class CommentToken : Token
    {
        public CommentToken(string value) : base(value)
        {
        }
    }

    public class CommentTextToken : Token
    {
        public CommentTextToken(string value) : base(value)
        {
        }
    }

    public class LineContinuationToken : Token
    {
        public LineContinuationToken(string value) : base(value)
        {
        }
    }
}
