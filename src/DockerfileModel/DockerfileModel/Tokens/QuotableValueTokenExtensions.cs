namespace DockerfileModel.Tokens
{
    public static class QuotableValueTokenExtensions
    {
        public static string ToString(this IQuotableValueToken token, bool excludeQuotes, bool excludeLineContinuations) =>
            TokenHelper.ToString(token, excludeQuotes, excludeLineContinuations);
    }
}
