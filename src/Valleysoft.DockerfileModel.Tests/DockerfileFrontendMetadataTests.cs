namespace Valleysoft.DockerfileModel.Tests;

public class DockerfileFrontendMetadataTests
{
    private const string Sha256 = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("")]
    [InlineData("FROM alpine\n")]
    [InlineData("# an ordinary comment\nFROM alpine\n")]
    [InlineData("# escape=`\nFROM alpine\n")]
    public void MissingSyntaxUsesBundledFrontend(string text)
    {
        DockerfileFrontendMetadata metadata = Dockerfile.Parse(text).Frontend;

        Assert.Equal(DockerfileFrontendKind.Bundled, metadata.Kind);
        Assert.Null(metadata.Reference);
        AssertNoComponents(metadata);
    }

    [Theory]
    [InlineData("1", "1", DockerfileFrontendChannel.Stable)]
    [InlineData("1.20", "1.20", DockerfileFrontendChannel.Stable)]
    [InlineData("1.20.0", "1.20.0", DockerfileFrontendChannel.Stable)]
    [InlineData("1.0.0", "1.0.0", DockerfileFrontendChannel.Stable)]
    [InlineData("99.100.200", "99.100.200", DockerfileFrontendChannel.Stable)]
    [InlineData("99999999999999999999", "99999999999999999999", DockerfileFrontendChannel.Stable)]
    [InlineData("01.020.000", "01.020.000", DockerfileFrontendChannel.Stable)]
    [InlineData("1-labs", "1", DockerfileFrontendChannel.Labs)]
    [InlineData("1.20-labs", "1.20", DockerfileFrontendChannel.Labs)]
    [InlineData("1.20.0-labs", "1.20.0", DockerfileFrontendChannel.Labs)]
    [InlineData("99.100.200-labs", "99.100.200", DockerfileFrontendChannel.Labs)]
    [InlineData("labs", null, DockerfileFrontendChannel.Labs)]
    public void OfficialVersionIsLexicalMetadata(string tag, string? version,
        DockerfileFrontendChannel channel)
    {
        string reference = $"docker/dockerfile:{tag}";
        DockerfileFrontendMetadata metadata = new SyntaxDirective(reference).Frontend;

        Assert.Equal(DockerfileFrontendKind.Official, metadata.Kind);
        Assert.Equal(reference, metadata.Reference);
        Assert.Equal("docker/dockerfile", metadata.Image);
        Assert.Equal(tag, metadata.Tag);
        Assert.Null(metadata.Digest);
        Assert.Equal(version, metadata.Version);
        Assert.Equal(channel, metadata.Channel);
    }

    [Theory]
    [InlineData("docker/dockerfile")]
    [InlineData("docker.io/docker/dockerfile")]
    [InlineData("index.docker.io/docker/dockerfile")]
    [InlineData("DOCKER.IO/docker/dockerfile")]
    [InlineData("INDEX.DOCKER.IO/docker/dockerfile")]
    public void OfficialIdentityDoesNotRewriteImage(string image)
    {
        string reference = $"{image}:1.20-labs";
        DockerfileFrontendMetadata metadata = new SyntaxDirective(reference).Frontend;

        Assert.Equal(DockerfileFrontendKind.Official, metadata.Kind);
        Assert.Equal(reference, metadata.Reference);
        Assert.Equal(image, metadata.Image);
        Assert.Equal("1.20-labs", metadata.Tag);
        Assert.Equal("1.20", metadata.Version);
        Assert.Equal(DockerfileFrontendChannel.Labs, metadata.Channel);
    }

    [Theory]
    [InlineData("docker/dockerfile-upstream")]
    [InlineData("docker/dockerfile-custom")]
    [InlineData("docker/dockerfiles")]
    [InlineData("example.com/docker/dockerfile")]
    [InlineData("docker.io.example.com/docker/dockerfile")]
    [InlineData("docker.io/docker/dockerfile/extra")]
    [InlineData("docker.io/other/dockerfile")]
    [InlineData("registry-1.docker.io/docker/dockerfile")]
    [InlineData("docker.io:443/docker/dockerfile")]
    [InlineData("DOCKER/dockerfile")]
    [InlineData("dockerfile")]
    public void OfficialLookalikesAreCustom(string image)
    {
        DockerfileFrontendMetadata metadata = new SyntaxDirective($"{image}:1.20").Frontend;

        Assert.Equal(DockerfileFrontendKind.Custom, metadata.Kind);
        Assert.Equal(image, metadata.Image);
        Assert.Equal("1.20", metadata.Tag);
        Assert.Equal("1.20", metadata.Version);
        Assert.Null(metadata.Channel);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("stable")]
    [InlineData("next")]
    [InlineData("v1.20.0")]
    [InlineData("1.20.0-rc1")]
    [InlineData("1.20-Labs")]
    [InlineData("labs-1.20")]
    [InlineData("latest-labs")]
    [InlineData("LABS")]
    [InlineData("1.20.0.1")]
    [InlineData("1..20")]
    [InlineData("1.")]
    public void UnknownOfficialTagsHaveNoInferredVersionOrChannel(string tag)
    {
        DockerfileFrontendMetadata metadata = new SyntaxDirective($"docker/dockerfile:{tag}").Frontend;

        Assert.Equal(DockerfileFrontendKind.Official, metadata.Kind);
        Assert.Equal(tag, metadata.Tag);
        Assert.Null(metadata.Version);
        Assert.Null(metadata.Channel);
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("1.20", "1.20")]
    [InlineData("1.20.0", "1.20.0")]
    [InlineData("nightly", null)]
    [InlineData("labs", null)]
    [InlineData("1.20-labs", null)]
    [InlineData("latest", null)]
    public void CustomTagsDoNotClaimOfficialChannels(string tag, string? version)
    {
        DockerfileFrontendMetadata metadata = new SyntaxDirective($"localhost:5000/team/frontend:{tag}").Frontend;

        Assert.Equal(DockerfileFrontendKind.Custom, metadata.Kind);
        Assert.Equal("localhost:5000/team/frontend", metadata.Image);
        Assert.Equal(tag, metadata.Tag);
        Assert.Equal(version, metadata.Version);
        Assert.Null(metadata.Channel);
    }

    [Theory]
    [InlineData("docker/dockerfile", DockerfileFrontendKind.Official)]
    [InlineData("index.docker.io/docker/dockerfile", DockerfileFrontendKind.Official)]
    [InlineData("localhost:5000/frontend", DockerfileFrontendKind.Custom)]
    [InlineData("[2001:db8::1]:5000/frontend", DockerfileFrontendKind.Custom)]
    public void UntaggedReferencesDoNotInferLatest(string image, DockerfileFrontendKind kind)
    {
        DockerfileFrontendMetadata metadata = new SyntaxDirective(image).Frontend;

        Assert.Equal(kind, metadata.Kind);
        Assert.Equal(image, metadata.Reference);
        Assert.Equal(image, metadata.Image);
        Assert.Null(metadata.Tag);
        Assert.Null(metadata.Digest);
        Assert.Null(metadata.Version);
        Assert.Null(metadata.Channel);
    }

    [Theory]
    [InlineData("docker/dockerfile", DockerfileFrontendKind.Official)]
    [InlineData("registry.example.com:5000/team/frontend", DockerfileFrontendKind.Custom)]
    public void DigestOnlyDoesNotInferVersion(string image, DockerfileFrontendKind kind)
    {
        string reference = $"{image}@{Sha256}";
        DockerfileFrontendMetadata metadata = new SyntaxDirective(reference).Frontend;

        Assert.Equal(kind, metadata.Kind);
        Assert.Equal(reference, metadata.Reference);
        Assert.Equal(image, metadata.Image);
        Assert.Null(metadata.Tag);
        Assert.Equal(Sha256, metadata.Digest);
        Assert.Null(metadata.Version);
        Assert.Null(metadata.Channel);
    }

    [Theory]
    [InlineData("docker/dockerfile", DockerfileFrontendKind.Official, DockerfileFrontendChannel.Stable)]
    [InlineData("registry.example.com:5000/team/frontend", DockerfileFrontendKind.Custom, null)]
    public void TagAndDigestAreBothPreserved(string image, DockerfileFrontendKind kind,
        DockerfileFrontendChannel? channel)
    {
        string reference = $"{image}:1.20@{Sha256}";
        DockerfileFrontendMetadata metadata = new SyntaxDirective(reference).Frontend;

        Assert.Equal(kind, metadata.Kind);
        Assert.Equal(reference, metadata.Reference);
        Assert.Equal(image, metadata.Image);
        Assert.Equal("1.20", metadata.Tag);
        Assert.Equal(Sha256, metadata.Digest);
        Assert.Equal("1.20", metadata.Version);
        Assert.Equal(channel, metadata.Channel);
    }

    [Theory]
    [InlineData("sha256", 64)]
    [InlineData("sha384", 96)]
    [InlineData("sha512", 128)]
    public void DigestMetadataIsNotIdentityVerification(string algorithm, int length)
    {
        string digest = $"{algorithm}:{new string('0', length)}";
        DockerfileFrontendMetadata metadata = new SyntaxDirective($"docker/dockerfile:99-labs@{digest}").Frontend;

        Assert.Equal(DockerfileFrontendKind.Official, metadata.Kind);
        Assert.Equal(digest, metadata.Digest);
        Assert.Equal("99", metadata.Version);
        Assert.Equal(DockerfileFrontendChannel.Labs, metadata.Channel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("${FRONTEND}")]
    [InlineData("docker/dockerfile:$VERSION")]
    [InlineData("docker/dockerfile:${VERSION:-1}")]
    [InlineData("https://docker.io/docker/dockerfile:1")]
    [InlineData("docker/dockerfile:")]
    [InlineData("docker/dockerfile@")]
    [InlineData("docker/dockerfile@sha256:abc")]
    [InlineData("docker/dockerfile:1@sha256:abc")]
    [InlineData("docker//dockerfile:1")]
    [InlineData("docker.io/Docker/dockerfile:1")]
    [InlineData("docker/dockerfile:1\tadditional")]
    [InlineData("docker/dockerfile:1\u00a0additional")]
    public void UnresolvedReferencesDoNotInventComponents(string reference)
    {
        SyntaxDirective directive = new("placeholder");
        directive.DirectiveValueToken.Value = reference.Length == 0 ? " " : reference;
        DockerfileFrontendMetadata metadata = directive.Frontend;

        Assert.Equal(DockerfileFrontendKind.Unresolved, metadata.Kind);
        Assert.Equal(reference, metadata.Reference);
        AssertNoComponents(metadata);
    }

    [Fact]
    public void MalformedDigestDoesNotLeakOtherwiseValidComponents()
    {
        string reference = $"docker/dockerfile:1@{Sha256}extra";
        DockerfileFrontendMetadata metadata = new SyntaxDirective(reference).Frontend;

        Assert.Equal(DockerfileFrontendKind.Unresolved, metadata.Kind);
        Assert.Equal(reference, metadata.Reference);
        AssertNoComponents(metadata);
    }

    [Fact]
    public void OverlongReferenceComponentsAreUnresolved()
    {
        foreach (string reference in new[]
        {
            $"{new string('a', 256)}:1",
            $"docker/dockerfile:{new string('1', 129)}"
        })
        {
            DockerfileFrontendMetadata metadata = new SyntaxDirective(reference).Frontend;

            Assert.Equal(DockerfileFrontendKind.Unresolved, metadata.Kind);
            Assert.Equal(reference, metadata.Reference);
            AssertNoComponents(metadata);
        }
    }

    [Theory]
    [InlineData("docker/dockerfile:1.20 explanatory text", "docker/dockerfile:1.20", DockerfileFrontendKind.Official)]
    [InlineData("docker/dockerfile:1.20 ${IGNORED}", "docker/dockerfile:1.20", DockerfileFrontendKind.Official)]
    [InlineData("example.com/frontend:nightly ignored", "example.com/frontend:nightly", DockerfileFrontendKind.Custom)]
    [InlineData("${FRONTEND} ignored", "${FRONTEND}", DockerfileFrontendKind.Unresolved)]
    public void OnlyAsciiSpaceSeparatesEffectiveReference(string value, string reference,
        DockerfileFrontendKind kind)
    {
        SyntaxDirective directive = new(value);
        string text = $"# syntax={value}\nFROM alpine\n";
        Dockerfile dockerfile = Dockerfile.Parse(text);

        Assert.Equal(reference, directive.Frontend.Reference);
        Assert.Equal(reference, dockerfile.Frontend.Reference);
        Assert.Equal(kind, directive.Frontend.Kind);
        Assert.Equal(kind, dockerfile.Frontend.Kind);
        Assert.Equal(value, directive.DirectiveValue);
        Assert.Equal(text, dockerfile.ToString());
    }

    [Fact]
    public void MetadataIsAnImmutableFreshSnapshot()
    {
        SyntaxDirective directive = new("docker/dockerfile:1.20");
        DockerfileFrontendMetadata original = directive.Frontend;
        Assert.NotSame(original, directive.Frontend);

        directive.DirectiveValue = $"example.com/frontend:nightly@{Sha256}";
        DockerfileFrontendMetadata updated = directive.Frontend;

        Assert.Equal(DockerfileFrontendKind.Official, original.Kind);
        Assert.Equal("docker/dockerfile:1.20", original.Reference);
        Assert.Equal("docker/dockerfile", original.Image);
        Assert.Equal("1.20", original.Tag);
        Assert.Null(original.Digest);
        Assert.Equal("1.20", original.Version);
        Assert.Equal(DockerfileFrontendChannel.Stable, original.Channel);

        Assert.NotSame(original, updated);
        Assert.Equal(DockerfileFrontendKind.Custom, updated.Kind);
        Assert.Equal($"example.com/frontend:nightly@{Sha256}", updated.Reference);
        Assert.Equal("example.com/frontend", updated.Image);
        Assert.Equal("nightly", updated.Tag);
        Assert.Equal(Sha256, updated.Digest);
        Assert.Null(updated.Version);
        Assert.Null(updated.Channel);
    }

    private static void AssertNoComponents(DockerfileFrontendMetadata metadata)
    {
        Assert.Null(metadata.Image);
        Assert.Null(metadata.Tag);
        Assert.Null(metadata.Digest);
        Assert.Null(metadata.Version);
        Assert.Null(metadata.Channel);
    }
}
