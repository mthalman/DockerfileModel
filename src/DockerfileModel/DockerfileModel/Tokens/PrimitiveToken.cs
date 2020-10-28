namespace DockerfileModel.Tokens
{
    public abstract class PrimitiveToken : Token, IValueToken
    {
        public PrimitiveToken(string value)
        {
            this.Value = value;
        }

        public virtual string Value { get; set; }

        protected override string GetUnderlyingValue(TokenStringOptions options) => Value;
    }
}
