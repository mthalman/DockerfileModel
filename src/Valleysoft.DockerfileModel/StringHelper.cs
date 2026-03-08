namespace Valleysoft.DockerfileModel;

internal static class StringHelper
{
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
        if (value.Length > 0 && value[0] != '\"' && value.Last() != '\"' && value.Contains(' ') && !value.Contains("\\ "))
        {
            value = "\"" + value + "\"";
        }

        return $"{key}={value}";
    }
}
