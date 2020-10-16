namespace DockerfileModel.Tokens
{
    public abstract class Token
    {
    }

    public abstract class PrimitiveToken : Token
    {
        public PrimitiveToken(string value)
        {
            this.Value = value;
        }

        public virtual string Value { get; set; }

        public override string ToString() => this.Value;
    }

    public class KeywordToken : PrimitiveToken
    {
        public KeywordToken(string value) : base(value)
        {
        }
    }

    public class SymbolToken : PrimitiveToken
    {
        public SymbolToken(string value) : base(value)
        {
        }
    }

    public abstract class QuotablePrimitiveToken : PrimitiveToken
    {
        public QuotablePrimitiveToken(string value) : base(value)
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

    public class IdentifierToken : QuotablePrimitiveToken
    {
        public IdentifierToken(string value) : base(value)
        {
        }
    }

    public class LiteralToken : QuotablePrimitiveToken
    {
        public LiteralToken(string value) : base(value)
        {
        }
    }

    public class WhitespaceToken : PrimitiveToken
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

    public class LineContinuationToken : SymbolToken
    {
        public LineContinuationToken(string value) : base(value)
        {
        }
    }
}
