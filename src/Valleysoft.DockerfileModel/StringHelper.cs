namespace Valleysoft.DockerfileModel;

internal static class StringHelper
{
    public static string FormatAsJson(IEnumerable<string> values)
    {
        Requires.NotNull(values, nameof(values));
        if (values.Any(v => v is null))
        {
            throw new ArgumentNullException(nameof(values), "Collection must not contain null elements.");
        }

        return $"[{String.Join(", ", values.Select(val => $"\"{val}\"").ToArray())}]";
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
