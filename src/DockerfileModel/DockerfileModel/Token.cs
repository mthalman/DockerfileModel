namespace DockerfileModel
{
    public abstract class Token
    {
        public Token(string value)
        {
            this.Value = value;
        }

        public virtual string Value { get; set; }

        public override string ToString() => this.Value;
    }

    public class KeywordToken : Token
    {
        public KeywordToken(string value) : base(value)
        {
        }
    }

    public class PunctuationToken : Token
    {
        public PunctuationToken(string value) : base(value)
        {
        }
    }

    public abstract class QuotableToken : Token
    {
        public QuotableToken(string value) : base(value)
        {
        }

        public char? QuoteChar { get; set; }

        public override string ToString()
        {
            if (QuoteChar.HasValue)
            {
                return $"{QuoteChar}{Value}{QuoteChar}";
            }

            return base.ToString();
        }
    }

    public class IdentifierToken : QuotableToken
    {
        public IdentifierToken(string value) : base(value)
        {
        }
    }

    public class LiteralToken : QuotableToken
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

    public class LineContinuationToken : Token
    {
        public LineContinuationToken(string value) : base(value)
        {
        }
    }
}
