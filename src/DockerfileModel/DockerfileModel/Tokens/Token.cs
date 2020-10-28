namespace DockerfileModel.Tokens
{
    public abstract class Token
    {
        public sealed override string ToString()
        {
            return ToString(new TokenStringOptions());
        }

        public string ToString(TokenStringOptions options)
        {
            string value = GetUnderlyingValue(options);

            if (!options.ExcludeQuotes && this is IQuotableToken quotableToken)
            {
                return $"{quotableToken.QuoteChar}{value}{quotableToken.QuoteChar}";
            }

            return value;
        }

        protected abstract string GetUnderlyingValue(TokenStringOptions options);
    }
}
