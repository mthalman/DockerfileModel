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
        string decoded = Decode(logical, offsets, out List<int> starts, out List<int> ends);
        (Token Token, string Text)[] source = path.Tokens.Select(token => (token, token.ToString())).ToArray();

        List<(int Start, int End, string Value, bool Inline)> replacements = new();
        ResolutionOptions valueOptions = new() { RemoveEscapeCharacters = options.RemoveEscapeCharacters };
        TextParser<VariableRefToken> variableParser = VariableRefToken.GetParser(escapeChar);
        TextSpan input = new(decoded);
        int tokenIndex = 0;
        int tokenStart = 1;
        char? quote = null;
        while (!input.IsAtEnd)
        {
            int index = input.Position.Absolute;
            while (tokenIndex < source.Length && tokenStart + source[tokenIndex].Text.Length <= starts[index])
            {
                tokenStart += source[tokenIndex].Text.Length;
                tokenIndex++;
            }
            if (tokenIndex < source.Length && path.IsResolvedJsonLiteral(source[tokenIndex].Token))
            {
                int end = index + 1;
                int tokenEnd = tokenStart + source[tokenIndex].Text.Length;
                while (end < decoded.Length && starts[end] < tokenEnd)
                {
                    end++;
                }
                if (options.RemoveEscapeCharacters)
                {
                    string literal = decoded.Substring(index, end - index);
                    string formatted = options.FormatValue(escapeChar, literal);
                    if (formatted != literal)
                    {
                        replacements.Add((starts[index], ends[end - 1], Encode(formatted), false));
                    }
                }
                input = input.Skip(end - index);
                continue;
            }
            char current = decoded[index];
            if (quote == '\'')
            {
                if (current == '\'')
                {
                    quote = null;
                }
                input = input.Skip(1);
                continue;
            }
            if (current == escapeChar && index + 1 < decoded.Length)
            {
                if (options.RemoveEscapeCharacters)
                {
                    replacements.Add((starts[index], ends[index + 1], Encode(decoded[index + 1].ToString()), false));
                }
                input = input.Skip(2);
                continue;
            }
            if (current == '"' || (current == '\'' && quote is null))
            {
                quote = quote == current ? null : current;
            }
            if (expandVariables && current == '$')
            {
                Result<VariableRefToken> variable = variableParser(input);
                if (variable.HasValue)
                {
                    int end = variable.Remainder.Position.Absolute;
                    string value = variable.Value.ResolveVariables(escapeChar, variables, valueOptions) ?? string.Empty;
                    replacements.Add((starts[index], ends[end - 1], Encode(value), true));
                    input = variable.Remainder;
                    continue;
                }
                if (index + 1 < decoded.Length && decoded[index + 1] == '{')
                {
                    VariableRefToken.Parse(input.ToString(), escapeChar);
                }
            }
            input = input.Skip(1);
        }

        if (replacements.Count == 0)
        {
            return physical;
        }
        List<Token> substitutions = new();
        bool inlineOnly = replacements.All(replacement => replacement.Inline);
        List<Token> tokens = BuildTokens(source, replacements, physical.Length - 1,
            options.UpdateInline && inlineOnly ? substitutions : null);
        string result = "\"" + string.Concat(tokens.Select(token => token.ToString())) + "\"";
        if (options.UpdateInline && replacements.Any(replacement => replacement.Inline))
        {
            path.ReplaceWithTokens(inlineOnly
                ? tokens
                : BuildTokens(source, replacements.Where(replacement => replacement.Inline), physical.Length - 1,
                    substitutions));
            path.RecordResolvedJsonLiterals(substitutions);
        }
        return result;
    }

    private static string Decode(string logical, List<int> offsets, out List<int> starts, out List<int> ends)
    {
        List<char> decoded = new();
        // Physical spans keep untouched JSON escapes and Dockerfile trivia intact.
        List<int> characterStarts = new();
        List<int> characterEnds = new();
        void AddCharacter(char value, int index, int length)
        {
            decoded.Add(value);
            characterStarts.Add(offsets[index]);
            characterEnds.Add(offsets[index + length - 1] + 1);
        }

        for (int index = 1; index < logical.Length - 1;)
        {
            int length = logical[index] == '\\' ? logical[index + 1] == 'u' ? 6 : 2 : 1;
            char value = length == 1 ? logical[index] : logical[index + 1] switch
            {
                'u' => (char)Convert.ToInt32(logical.Substring(index + 2, 4), 16),
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => logical[index + 1]
            };
            if (char.IsHighSurrogate(value))
            {
                int next = index + length;
                int nextLength = length == 6 && next + 6 < logical.Length &&
                    logical[next] == '\\' && logical[next + 1] == 'u' ? 6 : length == 1 ? 1 : 0;
                char low = nextLength == 6
                    ? (char)Convert.ToInt32(logical.Substring(next + 2, 4), 16)
                    : next < logical.Length - 1 ? logical[next] : '\0';
                if (nextLength > 0 && char.IsLowSurrogate(low))
                {
                    AddCharacter(value, index, length);
                    AddCharacter(low, next, nextLength);
                    index = next + nextLength;
                    continue;
                }
            }
            // BuildKit uses Go's JSON decoder, which replaces unmatched surrogates rather than rejecting them.
            AddCharacter(char.IsSurrogate(value) ? '\uFFFD' : value, index, length);
            index += length;
        }
        starts = characterStarts;
        ends = characterEnds;
        return new string(decoded.ToArray());
    }

    private static string Encode(string value) => JsonEncodedText.Encode(value).ToString();

    private static List<Token> BuildTokens((Token Token, string Text)[] source,
        IEnumerable<(int Start, int End, string Value, bool Inline)> replacements, int end,
        List<Token>? substitutions = null)
    {
        List<Token> tokens = new();
        int tokenIndex = 0;
        int tokenStart = 1;
        void CopyUnchanged(int start, int stop)
        {
            while (tokenIndex < source.Length && tokenStart < stop)
            {
                (Token token, string text) = source[tokenIndex];
                int tokenEnd = tokenStart + text.Length;
                int first = Math.Max(start - tokenStart, 0);
                int last = Math.Min(stop - tokenStart, text.Length);
                if (first < last)
                {
                    tokens.Add(first == 0 && last == text.Length
                        ? token
                        : new StringToken(text.Substring(first, last - first)));
                }
                if (tokenEnd > stop)
                {
                    break;
                }
                tokenStart = tokenEnd;
                tokenIndex++;
            }
        }

        int position = 1;
        foreach ((int start, int finish, string value, bool inline) in replacements)
        {
            CopyUnchanged(position, start);
            StringToken substitution = new(value);
            tokens.Add(substitution);
            if (inline)
            {
                substitutions?.Add(substitution);
            }
            position = finish;
        }
        CopyUnchanged(position, end);
        return tokens;
    }
}
