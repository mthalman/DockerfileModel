namespace Valleysoft.DockerfileModel;

internal static class StringHelper
{
    private const char DoubleQuote = '"';
    private const char SingleQuote = '\'';

    public static string FormatAsJson(IEnumerable<string> values)
    {
        Requires.NotNull(values, nameof(values));

        // Materialize the sequence once to avoid double enumeration, then
        // validate that no element is null.
        var materializedValues = values as IList<string> ?? values.ToList();
        foreach (string? value in materializedValues)
        {
            if (value is null)
            {
                throw new ArgumentException("Sequence cannot contain null values.", nameof(values));
            }
        }

        return $"[{String.Join(", ", materializedValues.Select(val => $"\"{val}\""))}]";
    }

    /// <summary>
    /// Formats a key=value assignment, wrapping the value in quotes if it contains unescaped spaces.
    /// </summary>
    public static string FormatKeyValueAssignment(string key, string value)
    {
        if (ShouldWrapValue(value))
        {
            char wrappingQuote = SelectWrappingQuote(value);
            value = wrappingQuote + EscapeQuote(value, wrappingQuote) + wrappingQuote;
        }

        return $"{key}={value}";
    }

    private static bool ShouldWrapValue(string value) =>
        value.Length > 0 &&
        !IsWrappedInMatchingQuotes(value) &&
        value.Contains(' ') &&
        !value.Contains("\\ ");

    private static bool IsWrappedInMatchingQuotes(string value) =>
        value.Length > 1 &&
        ((value[0] == DoubleQuote && value[value.Length - 1] == DoubleQuote) ||
         (value[0] == SingleQuote && value[value.Length - 1] == SingleQuote));

    private static char SelectWrappingQuote(string value)
    {
        if (!value.Contains(DoubleQuote))
        {
            return DoubleQuote;
        }

        if (!value.Contains(SingleQuote))
        {
            return SingleQuote;
        }

        return DoubleQuote;
    }

    private static string EscapeQuote(string value, char quoteChar) =>
        value.Replace(quoteChar.ToString(), $"\\{quoteChar}");
}
