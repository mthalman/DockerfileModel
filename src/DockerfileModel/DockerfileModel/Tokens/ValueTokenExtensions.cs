namespace DockerfileModel.Tokens
{
    public static class ValueTokenExtensions
    {
        public static string ToString(this IValueToken token, bool excludeLineContinuations = false) =>
            TokenHelper.ToString(token, excludeLineContinuations);
    }
}
