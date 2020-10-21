namespace DockerfileModel.Tokens
{
    public static class QuotableTokenExtensions
    {
        public static string ToString(this IQuotableToken token, bool excludeQuotes) =>
            TokenHelper.ToString(token, excludeQuotes);
    }
}
