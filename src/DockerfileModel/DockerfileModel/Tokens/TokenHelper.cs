using System.Collections.Generic;
using System.Text;

namespace DockerfileModel.Tokens
{
    internal static class TokenHelper
    {
        public static string ToString(IValueToken token, bool excludeLineContinuations)
        {
            if (token is AggregateToken aggregate)
            {
                return aggregate.GetTokensString(excludeLineContinuations);
            }
            else
            {
                return token.ToString();
            }
        }

        public static string ToString(IQuotableToken token, bool excludeQuotes)
        {
            string value;
            if (token is AggregateToken aggregate)
            {
                value = aggregate.GetTokensString(excludeLineContinuations: false);
            }
            else
            {
                value = token.ToString();
            }

            return FormatQuotableToken(token, value, excludeQuotes);
        }

        public static string ToString(IQuotableValueToken token, bool excludeQuotes, bool excludeLineContinuations) =>
            FormatQuotableToken(token, ToString((IValueToken)token, excludeLineContinuations), excludeQuotes);

        public static IEnumerable<Token> CollapseStringTokens(IEnumerable<Token> tokens)
        {
            List<Token> result = new List<Token>();
            StringBuilder builder = new StringBuilder();
            foreach (Token token in tokens)
            {
                if (token is StringToken stringToken)
                {
                    builder.Append(stringToken.Value);
                }
                else
                {
                    if (builder.Length > 0)
                    {
                        result.Add(new StringToken(builder.ToString()));
                        builder = new StringBuilder();
                    }

                    result.Add(token);
                }
            }

            if (builder.Length > 0)
            {
                result.Add(new StringToken(builder.ToString()));
            }

            return result;
        }

        private static string FormatQuotableToken(IQuotableToken token, string value, bool excludeQuotes)
        {
            if (!excludeQuotes)
            {
                return $"{token.QuoteChar}{value}{token.QuoteChar}";
            }

            return value;
        }
    }
}
