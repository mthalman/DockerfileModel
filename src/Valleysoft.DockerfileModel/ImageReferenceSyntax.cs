using System.Text.RegularExpressions;

namespace Valleysoft.DockerfileModel;

internal static class ImageReferenceSyntax
{
    private const string Component = @"[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*";
    private const string HostPart = @"[a-zA-Z0-9](?:[a-zA-Z0-9-]*[a-zA-Z0-9])?";
    private static readonly Regex Name = new(
        @"\A(?:(?:" + HostPart + @"(?:\." + HostPart +
        @")*|\[[a-fA-F0-9:]+\])(?::[0-9]+)?/)?" + Component + @"(?:/" + Component +
        @")*\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Tag = new(@"\A[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,127}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // ImageName is an editable component model, not a full reference validator: it accepts
    // partial parses and cannot represent a tag and digest together.
    public static bool IsValid(string value)
    {
        int digestSeparator = value.IndexOf('@');
        string nameAndTag = digestSeparator < 0 ? value : value.Substring(0, digestSeparator);
        if (digestSeparator >= 0 && !IsValidDigest(value.Substring(digestSeparator + 1)))
        {
            return false;
        }
        int tagSeparator = nameAndTag.LastIndexOf(':');
        string name = nameAndTag;
        if (tagSeparator > nameAndTag.LastIndexOf('/'))
        {
            string tag = nameAndTag.Substring(tagSeparator + 1);
            if (tag.Length > 128 || !Tag.IsMatch(tag))
            {
                return false;
            }
            name = nameAndTag.Substring(0, tagSeparator);
        }
        return name.Length <= 255 && Name.IsMatch(name);
    }

    private static bool IsValidDigest(string value)
    {
        int separator = value.IndexOf(':');
        if (separator < 0)
        {
            return false;
        }
        int length = value.Substring(0, separator) switch
        {
            "sha256" => 64,
            "sha384" => 96,
            "sha512" => 128,
            _ => -1
        };
        return length > 0 && value.Length - separator - 1 == length &&
            value.Skip(separator + 1).All(ch => ch >= '0' && ch <= '9' || ch >= 'a' && ch <= 'f');
    }
}
