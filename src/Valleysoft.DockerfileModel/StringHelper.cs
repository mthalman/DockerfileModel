namespace Valleysoft.DockerfileModel;

internal static class StringHelper
{
    public static string FormatAsJson(IEnumerable<string> values)
    {
        Requires.NotNullEmptyOrNullElements(values, nameof(values));
        return $"[{String.Join(", ", values.Select(val => $"\"{val}\"").ToArray())}]";
    }
}
