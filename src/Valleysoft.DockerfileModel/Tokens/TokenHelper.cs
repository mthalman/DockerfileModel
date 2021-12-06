using System.Text;

namespace Valleysoft.DockerfileModel.Tokens;

internal static class TokenHelper
{
    public static IEnumerable<Token> CollapseStringTokens(IEnumerable<Token> tokens) =>
        CollapseTokens(tokens, token => token is StringToken, val => new StringToken(val));

    public static IEnumerable<Token> CollapseTokens(IEnumerable<Token> tokens, Func<Token, bool> collapseToken, Func<string, Token> createToken)
    {
        List<Token> result = new();
        StringBuilder builder = new();
        foreach (Token token in tokens)
        {
            if (collapseToken(token))
            {
                builder.Append(token.ToString());
            }
            else
            {
                if (builder.Length > 0)
                {
                    result.Add(createToken(builder.ToString()));
                    builder = new StringBuilder();
                }

                result.Add(token);
            }
        }

        if (builder.Length > 0)
        {
            result.Add(createToken(builder.ToString()));
        }

        return result;
    }
}
