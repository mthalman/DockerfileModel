using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DockerfileModel
{
    public static class ArgResolver
    {
        private const string LeadingGroup = "leading";
        private const string ArgGroup = "arg";
        private const string TrailingGroup = "trailing";

        public static string Resolve(string text, IDictionary<string, string?> argValues, char escapeChar) =>
            Regex.Replace(
                text,
                $@"(?<{LeadingGroup}>.*?(?<!{escapeChar.ToString().Replace("\\", "\\\\")}))(?<{ArgGroup}>\$\w+)(?<{TrailingGroup}>[^$]*)",
                match => $"{match.Groups[LeadingGroup]}{ReplaceArg(match.Groups[ArgGroup].Value, argValues)}{match.Groups[TrailingGroup]}");

        private static string? ReplaceArg(string arg, IDictionary<string, string?> argValues)
        {
            arg = arg.Substring(1);
            argValues.TryGetValue(arg, out string? value);
            return value;
        }
    }
}
