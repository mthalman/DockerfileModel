namespace DockerfileModel.Tokens
{
    public abstract class PrimitiveToken : Token, IValueToken
    {
        public PrimitiveToken(string value)
        {
            this.Value = value;
        }

        public virtual string Value { get; set; }

        public override string ToString() => this.Value;
    }
}
