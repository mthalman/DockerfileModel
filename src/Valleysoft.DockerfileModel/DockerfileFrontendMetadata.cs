namespace Valleysoft.DockerfileModel;

public enum DockerfileFrontendKind
{
    Bundled,
    Official,
    Custom,
    Unresolved
}

public enum DockerfileFrontendChannel
{
    Stable,
    Labs
}

/// <summary>
/// An immutable snapshot of a syntax directive's image reference, without resolving tags,
/// verifying image identity, or determining frontend feature support.
/// </summary>
public sealed class DockerfileFrontendMetadata
{
    private DockerfileFrontendMetadata(DockerfileFrontendKind kind, string? reference,
        string? image = null, string? tag = null, string? digest = null,
        string? version = null, DockerfileFrontendChannel? channel = null)
    {
        Kind = kind;
        Reference = reference;
        Image = image;
        Tag = tag;
        Digest = digest;
        Version = version;
        Channel = channel;
    }

    public DockerfileFrontendKind Kind { get; }

    /// <summary>
    /// The portion of the directive value before the first ASCII space, including when
    /// that portion cannot be parsed. Null indicates that no syntax directive is present.
    /// </summary>
    public string? Reference { get; }

    public string? Image { get; }

    public string? Tag { get; }

    /// <summary>
    /// The literal digest, including its algorithm prefix; no image identity is verified.
    /// </summary>
    public string? Digest { get; }

    /// <summary>
    /// Numeric tag text with its original component precision, or null when unrecognized.
    /// Floating tags are not resolved and missing version components are not inferred.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// A recognized official frontend tag channel, not a guarantee of feature support.
    /// Custom repositories and unrecognized or absent tags have no channel.
    /// </summary>
    public DockerfileFrontendChannel? Channel { get; }

    internal static DockerfileFrontendMetadata FromValue(string? value)
    {
        if (value is null)
        {
            return new DockerfileFrontendMetadata(DockerfileFrontendKind.Bundled, null);
        }

        int space = value.IndexOf(' ');
        string reference = space < 0 ? value : value.Substring(0, space);
        if (!ImageReferenceSyntax.IsValid(reference))
        {
            return new DockerfileFrontendMetadata(DockerfileFrontendKind.Unresolved, reference);
        }

        int digestSeparator = reference.IndexOf('@');
        string nameAndTag = digestSeparator < 0 ? reference : reference.Substring(0, digestSeparator);
        string? digest = digestSeparator < 0 ? null : reference.Substring(digestSeparator + 1);

        int tagSeparator = nameAndTag.LastIndexOf(':');
        bool hasTag = tagSeparator > nameAndTag.LastIndexOf('/');
        string image = hasTag ? nameAndTag.Substring(0, tagSeparator) : nameAndTag;
        string? tag = hasTag ? nameAndTag.Substring(tagSeparator + 1) : null;

        bool official = IsOfficialImage(image);
        string? version = IsNumericVersion(tag) ? tag : null;
        DockerfileFrontendChannel? channel = null;
        if (official)
        {
            if (version is not null)
            {
                channel = DockerfileFrontendChannel.Stable;
            }
            else if (tag == "labs")
            {
                channel = DockerfileFrontendChannel.Labs;
            }
            else if (tag is not null && tag.EndsWith("-labs", StringComparison.Ordinal))
            {
                string candidate = tag.Substring(0, tag.Length - "-labs".Length);
                if (IsNumericVersion(candidate))
                {
                    version = candidate;
                    channel = DockerfileFrontendChannel.Labs;
                }
            }
        }

        return new DockerfileFrontendMetadata(
            official ? DockerfileFrontendKind.Official : DockerfileFrontendKind.Custom,
            reference, image, tag, digest, version, channel);
    }

    private static bool IsOfficialImage(string image)
    {
        if (image == "docker/dockerfile")
        {
            return true;
        }

        int separator = image.IndexOf('/');
        if (separator < 0 || image.Substring(separator + 1) != "docker/dockerfile")
        {
            return false;
        }

        string registry = image.Substring(0, separator);
        return registry.Equals("docker.io", StringComparison.OrdinalIgnoreCase) ||
            registry.Equals("index.docker.io", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumericVersion(string? value)
    {
        if (String.IsNullOrEmpty(value))
        {
            return false;
        }

        int components = 1;
        bool hasDigit = false;
        foreach (char character in value!)
        {
            if (character >= '0' && character <= '9')
            {
                hasDigit = true;
            }
            else if (character == '.' && hasDigit && components < 3)
            {
                components++;
                hasDigit = false;
            }
            else
            {
                return false;
            }
        }

        return hasDigit;
    }
}
