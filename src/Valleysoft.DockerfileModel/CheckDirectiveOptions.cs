namespace Valleysoft.DockerfileModel;

/// <summary>An immutable projection of check settings, not a registry of available check names.</summary>
public sealed class CheckDirectiveOptions
{
    public CheckDirectiveOptions(IEnumerable<string>? skippedChecks = null, bool skipAll = false,
        bool warningsAsErrors = false, IEnumerable<string>? experimentalChecks = null, bool experimentalAll = false)
        : this(skippedChecks, skipAll, warningsAsErrors, experimentalChecks, experimentalAll, validateNames: true)
    {
    }

    private CheckDirectiveOptions(IEnumerable<string>? skippedChecks, bool skipAll,
        bool warningsAsErrors, IEnumerable<string>? experimentalChecks, bool experimentalAll, bool validateNames)
    {
        SkippedChecks = Snapshot(skippedChecks, nameof(skippedChecks), validateNames);
        ExperimentalChecks = Snapshot(experimentalChecks, nameof(experimentalChecks), validateNames);
        SkipAll = skipAll;
        WarningsAsErrors = warningsAsErrors;
        ExperimentalAll = experimentalAll;
    }

    public IReadOnlyList<string> SkippedChecks { get; }
    public bool SkipAll { get; }
    public bool WarningsAsErrors { get; }
    public IReadOnlyList<string> ExperimentalChecks { get; }
    public bool ExperimentalAll { get; }

    internal static bool TryParse(string value, out CheckDirectiveOptions? options, out string? error)
    {
        options = null;
        error = null;
        string[] skipped = Array.Empty<string>();
        string[] experimental = Array.Empty<string>();
        bool skipAll = false, experimentalAll = false, warningsAsErrors = false;

        // BuildKit extracts the first space-delimited part before parsing check options.
        int space = value.IndexOf(' ');
        string effectiveValue = (space < 0 ? value : value.Substring(0, space)).Trim();
        if (effectiveValue.Length > 0)
        {
            foreach (string part in effectiveValue.Split(new[] { ';' }, 3))
            {
                int separator = part.IndexOf('=');
                if (separator < 0)
                {
                    error = $"Invalid check option '{part}': expected '='.";
                    return false;
                }
                string key = part.Substring(0, separator).Trim();
                string argument = part.Substring(separator + 1).Trim();
                switch (key)
                {
                    case "skip":
                        if (argument == "all")
                        {
                            skipAll = true;
                        }
                        else
                        {
                            skipped = argument.Split(',').Select(name => name.Trim()).ToArray();
                        }
                        break;
                    case "experimental":
                        if (argument == "all")
                        {
                            experimentalAll = true;
                        }
                        else
                        {
                            experimental = argument.Split(',').Select(name => name.Trim()).ToArray();
                        }
                        break;
                    case "error":
                        switch (argument)
                        {
                            case "1": case "t": case "T": case "TRUE": case "true": case "True":
                                warningsAsErrors = true;
                                break;
                            case "0": case "f": case "F": case "FALSE": case "false": case "False":
                                warningsAsErrors = false;
                                break;
                            default:
                                error = $"Invalid boolean in check option '{part}'.";
                                return false;
                        }
                        break;
                    default:
                        error = $"Unknown check option '{key}'.";
                        return false;
                }
            }
        }
        options = new CheckDirectiveOptions(skipped, skipAll, warningsAsErrors, experimental, experimentalAll,
            validateNames: false);
        return true;
    }

    internal string Format()
    {
        List<string> parts = new();
        if (SkipAll || SkippedChecks.Count > 0)
        {
            parts.Add("skip=" + (SkipAll ? "all" : string.Join(",", SkippedChecks)));
        }
        if (ExperimentalAll || ExperimentalChecks.Count > 0)
        {
            parts.Add("experimental=" + (ExperimentalAll ? "all" : string.Join(",", ExperimentalChecks)));
        }
        parts.Add("error=" + (WarningsAsErrors ? "true" : "false"));
        return string.Join(";", parts);
    }

    private static IReadOnlyList<string> Snapshot(IEnumerable<string>? values, string parameter, bool validateNames)
    {
        string[] items = values?.ToArray() ?? Array.Empty<string>();
        if (items.Any(value => value is null))
        {
            throw new ArgumentException("Check names cannot be null.", parameter);
        }
        if (validateNames && items.Any(value => value.Length == 0 || value == "all" ||
            value.Any(c => char.IsWhiteSpace(c) || c is ',' or ';' or '=')))
        {
            throw new ArgumentException(
                "Check names must be nonempty and cannot contain whitespace or option separators. Use the all setting for 'all'.",
                parameter);
        }
        return Array.AsReadOnly(items);
    }
}
