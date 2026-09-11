using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class ArgumentValidationTests
{
    [Fact]
    public void NullOnlyGuards()
    {
        Assert.Throws<ArgumentNullException>("text", () => Dockerfile.Parse(null!));
        Assert.Throws<ArgumentNullException>("items", () => new Dockerfile(null!));
        Assert.Throws<ArgumentNullException>("dockerfile", () => new DockerfileBuilder(null!));
        Assert.Throws<ArgumentNullException>("values", () => StringHelper.FormatAsJson(null!));
        Assert.Throws<ArgumentNullException>("defaultArgs", () => new HealthCheckInstruction((IEnumerable<string>)null!));

        Assert.Empty(Dockerfile.Parse("").Items);
        Assert.Empty(new Dockerfile(Array.Empty<DockerfileConstruct>()).Items);
        Assert.Equal("[]", StringHelper.FormatAsJson(Array.Empty<string>()));
        Assert.Equal("", new StringToken("").Value);
        Assert.Equal(" ", new StringToken(" ").Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("\0name")]
    public void StringGuards(string? value)
    {
        Type exceptionType = value is null ? typeof(ArgumentNullException) : typeof(ArgumentException);
        AssertArgument(exceptionType, "imageName", () => new FromInstruction(value!));
        AssertArgument(exceptionType, "commandWithArgs", () => new HealthCheckInstruction(value!));
        AssertArgument(exceptionType, "variableName", () => new VariableRefToken(value!));
        AssertArgument(exceptionType, "repository", () => new ImageName(value!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0")]
    [InlineData("\0repo")]
    public void RepositoryRejectsEmptyWhitespaceAndLeadingNull(string repository)
    {
        Assert.Throws<ArgumentException>("repository", () => ImageName.FormatImageName(repository, null, null, null));
    }

    [Fact]
    public void NullOrEmptyDoesNotRejectWhitespaceOrEmbeddedNull()
    {
        Assert.Equal(" ", new WhitespaceToken(" ").Value);
        Assert.Equal("repo\0", ImageName.FormatImageName("repo\0", null, null, null));
    }

    [Theory]
    [InlineData("ARG", "args")]
    [InlineData("ENV", "variables")]
    [InlineData("LABEL", "variables")]
    public void DictionaryGuards(string instruction, string parameterName)
    {
        void Create(IDictionary<string, string?>? values)
        {
            _ = instruction switch
            {
                "ARG" => (Instruction)new ArgInstruction(values!),
                "ENV" => new EnvInstruction(values!),
                "LABEL" => new LabelInstruction(values!),
                _ => throw new InvalidOperationException()
            };
        }

        Assert.Throws<ArgumentNullException>(parameterName, () => Create(null));
        Assert.Throws<ArgumentException>(parameterName, () => Create(new Dictionary<string, string?>()));
        Create(new Dictionary<string, string?> { ["KEY"] = "value" });
    }

    public static TheoryData<string, string, int, bool> CollectionCases()
    {
        TheoryData<string, string, int, bool> cases = new();
        foreach ((string instruction, string parameterName) in new[]
        {
            ("VOLUME", "paths"), ("EXPOSE", "portSpecs"), ("COPY", "sources"), ("ADD", "sources")
        })
        {
            for (int input = 0; input < 5; input++)
            {
                cases.Add(instruction, parameterName, input, false);
                cases.Add(instruction, parameterName, input, true);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(CollectionCases))]
    public void CollectionGuards(string instruction, string parameterName, int input, bool lazy)
    {
        string[]? values = input switch
        {
            0 => null,
            1 => [],
            2 => [null!],
            3 => ["80", null!],
            4 => ["80", "443"],
            _ => throw new InvalidOperationException()
        };
        IEnumerable<string>? sequence = lazy && values is not null ? Enumerate(values) : values;
        void Create()
        {
            _ = instruction switch
            {
                "VOLUME" => (Instruction)new VolumeInstruction(sequence!),
                "EXPOSE" => new ExposeInstruction(sequence!),
                "COPY" => new CopyInstruction(sequence!, "/destination"),
                "ADD" => new AddInstruction(sequence!, "/destination"),
                _ => throw new InvalidOperationException()
            };
        }

        if (input == 4)
        {
            Create();
        }
        else
        {
            AssertArgument(input == 0 ? typeof(ArgumentNullException) : typeof(ArgumentException), parameterName, Create);
        }
    }

    [Fact]
    public void NullElementValidationIsDistinctFromStringValidation()
    {
        Assert.Equal("VOLUME [\"\"]", new VolumeInstruction(new[] { "" }).ToString());
        Assert.Throws<Sprache.ParseException>(() => new VolumeInstruction(new[] { " " }));
        Assert.Throws<ArgumentException>("values", () => StringHelper.FormatAsJson(new[] { "valid", null! }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\0new")]
    public void FailedSettersPreserveState(string? value)
    {
        Type exceptionType = value is null ? typeof(ArgumentNullException) : typeof(ArgumentException);
        FromInstruction from = new("scratch");
        CopyInstruction copy = new(new[] { "source" }, "/destination");
        ExposeInstruction expose = new("80");

        AssertArgument(exceptionType, "value", () => from.ImageName = value!);
        AssertArgument(exceptionType, "value", () => copy.Destination = value);
        AssertArgument(exceptionType, "value", () => expose.Ports[0] = value!);

        Assert.Equal("FROM scratch", from.ToString());
        Assert.Equal("COPY source /destination", copy.ToString());
        Assert.Equal("EXPOSE 80", expose.ToString());
    }

    [Fact]
    public void ExplicitParameterNameAndTokenSetters()
    {
        ArgInstruction arg = new("KEY", "value");
        FromInstruction from = new("scratch");
        Assert.Throws<ArgumentNullException>("value", () => arg.Args[0] = null!);
        Assert.Throws<ArgumentNullException>("value", () => from.ImageNameToken = null!);
        Assert.Equal("ARG KEY=value", arg.ToString());
        Assert.Equal("FROM scratch", from.ToString());
    }

    [Fact]
    public void ValidationOrder()
    {
        Assert.Throws<ArgumentNullException>("sources", () => new CopyInstruction((IEnumerable<string>)null!, null!));
        Assert.Throws<ArgumentException>("sources", () => new CopyInstruction(Array.Empty<string>(), null!));
        Assert.Throws<ArgumentNullException>("command", () => new HealthCheckInstruction(null!, (IEnumerable<string>)null!));
        Assert.Throws<ArgumentNullException>("repository", () => ImageName.FormatImageName(null!, null, "tag", "sha256:digest"));
    }

    [Fact]
    public void InvalidOperationsPreserveState()
    {
        ImageName tagged = new("repo", tag: "tag");
        ImageName digested = new("repo", digest: "sha256:digest");
        Assert.Throws<InvalidOperationException>(() => tagged.Digest = "sha256:digest");
        Assert.Throws<InvalidOperationException>(() => digested.Tag = "tag");
        Assert.Equal("repo:tag", tagged.ToString());
        Assert.Equal("repo@sha256:digest", digested.ToString());
        Assert.Throws<InvalidOperationException>(() => ImageName.FormatImageName("repo", null, "tag", "sha256:digest"));
        Assert.Throws<InvalidOperationException>(() => new WhitespaceToken("text"));
        Assert.Throws<InvalidOperationException>(() => new VariableRefToken("name", "invalid", "value"));
    }

    private static void AssertArgument(Type exceptionType, string parameterName, Action action)
    {
        ArgumentException exception = Assert.IsAssignableFrom<ArgumentException>(Assert.Throws(exceptionType, action));
        Assert.Equal(parameterName, exception.ParamName);
    }

    private static IEnumerable<string> Enumerate(IEnumerable<string> values)
    {
        foreach (string value in values)
        {
            yield return value;
        }
    }
}
