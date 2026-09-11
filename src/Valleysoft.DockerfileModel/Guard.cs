using System.Diagnostics.CodeAnalysis;

namespace Valleysoft.DockerfileModel;

internal static class Guard
{
    public static void NotNull<T>([NotNull] T? value, string paramName) where T : class
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    public static void NotNullOrEmpty([NotNull] string? value, string paramName)
    {
        NotNull(value, paramName);
        if (value.Length == 0 || value[0] == '\0')
        {
            throw new ArgumentException("Value cannot be empty or start with a null character.", paramName);
        }
    }

    public static void NotNullOrWhiteSpace([NotNull] string? value, string paramName)
    {
        NotNullOrEmpty(value, paramName);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot consist only of whitespace.", paramName);
        }
    }

    public static void NotNullOrEmpty<T>([NotNull] IEnumerable<T>? values, string paramName)
    {
        NotNull(values, paramName);
        using IEnumerator<T> enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException("Collection cannot be empty.", paramName);
        }
    }

    public static void NotNullEmptyOrNullElements<T>([NotNull] IEnumerable<T?>? values, string paramName) where T : class
    {
        NotNull(values, paramName);
        using IEnumerator<T?> enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            throw new ArgumentException("Collection cannot be empty.", paramName);
        }

        do
        {
            if (enumerator.Current is null)
            {
                throw new ArgumentException("Collection cannot contain null elements.", paramName);
            }
        }
        while (enumerator.MoveNext());
    }

    public static void Operation(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
