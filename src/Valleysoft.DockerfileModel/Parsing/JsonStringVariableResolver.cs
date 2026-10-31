using System.Text.Json;
using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Parsing;

internal static class JsonStringVariableResolver
{
    internal static string Resolve(LiteralToken path, char escapeChar,
        IDictionary<string, string?> variables, ResolutionOptions options, bool expandVariables)
    {
        string physical = path.ToString();
        List<int> offsets = new();
        string logical = ConstructReader.FoldHeader(physical, 0, escapeChar, out int consumed, offsets);
        if (consumed != physical.Length)
        {
            throw new InvalidOperationException("The JSON path contains unconsumed physical input.");
        }
        using JsonDocument document = JsonDocument.Parse(logical);
        string decoded = document.RootElement.GetString()!;
        // Map decoded UTF-16 characters to physical spans so untouched escapes and trivia stay intact.
        List<int> starts = new();
        List<int> ends = new();
        for (int index = 1; index < logical.Length - 1;)
        {
            int length = logical[index] == '\\' ? logical[index + 1] == 'u' ? 6 : 2 : 1;
            starts.Add(offsets[index]);
            ends.Add(offsets[index + length - 1] + 1);
            index += length;
        }

        List<(int Start, int End, string Value)> replacements = new();
        ResolutionOptions valueOptions = new() { RemoveEscapeCharacters = options.RemoveEscapeCharacters };
        for (int index = 0; index < decoded.Length;)
        {
            if (decoded[index] == escapeChar && index + 1 < decoded.Length)
            {
                if (options.RemoveEscapeCharacters)
                {
                    replacements.Add((starts[index], ends[index + 1], Encode(decoded[index + 1].ToString())));
                }
                index += 2;
                continue;
            }
            if (expandVariables && decoded[index] == '$')
            {
                Result<VariableRefToken> variable = VariableRefToken.GetParser(escapeChar)
                    .TryParse(decoded.Substring(index));
                if (variable.HasValue)
                {
                    int length = variable.Remainder.Position.Absolute;
                    string value = variable.Value.ResolveVariables(escapeChar, variables, valueOptions) ?? string.Empty;
                    replacements.Add((starts[index], ends[index + length - 1], Encode(value)));
                    index += length;
                    continue;
                }
                if (index + 1 < decoded.Length && decoded[index + 1] == '{')
                {
                    VariableRefToken.Parse(decoded.Substring(index), escapeChar);
                }
            }
            index++;
        }

        List<Token> tokens = new();
        int position = 1;
        foreach ((int start, int end, string value) in replacements)
        {
            CopyUnchanged(path, position, start, tokens);
            tokens.Add(new StringToken(value));
            position = end;
        }
        CopyUnchanged(path, position, physical.Length - 1, tokens);
        string result = "\"" + string.Concat(tokens.Select(token => token.ToString())) + "\"";
        if (options.UpdateInline)
        {
            path.ReplaceWithTokens(TokenHelper.CollapseStringTokens(tokens));
        }
        return result;
    }

    private static string Encode(string value) => JsonEncodedText.Encode(value).ToString();

    private static void CopyUnchanged(LiteralToken path, int start, int end, List<Token> destination)
    {
        int position = 1;
        foreach (Token token in path.Tokens)
        {
            string text = token.ToString();
            int first = Math.Max(start - position, 0);
            int last = Math.Min(end - position, text.Length);
            if (first < last)
            {
                destination.Add(first == 0 && last == text.Length &&
                    token is LineContinuationToken or CommentToken or WhitespaceToken
                    ? token
                    : new StringToken(text.Substring(first, last - first)));
            }
            position += text.Length;
            if (position >= end)
            {
                break;
            }
        }
    }
}
