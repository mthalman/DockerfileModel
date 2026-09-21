using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel.Tests;

public class ScenarioTests
{
    /// <summary>
    /// Updates application inputs, cache options, and exposed ports through list edits while preserving surrounding text.
    /// </summary>
    [Fact]
    public void EditApplicationDockerfileListsWithoutRebuildingInstructions()
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "# syntax=docker/dockerfile:1\n" +
            "FROM alpine:3.22\n" +
            "# Application setup\n" +
            "COPY src/ obsolete/ /app/\n" +
            "RUN --mount=type=cache,target=/var/cache/apk apk add --no-cache curl\n" +
            "EXPOSE 80\n" +
            "CMD [\"sh\"]\n");
        CopyInstruction copy = Assert.Single(dockerfile.Items.OfType<CopyInstruction>());
        RunInstruction run = Assert.Single(dockerfile.Items.OfType<RunInstruction>());
        ExposeInstruction expose = Assert.Single(dockerfile.Items.OfType<ExposeInstruction>());
        FromInstruction from = Assert.Single(dockerfile.Items.OfType<FromInstruction>());
        CmdInstruction cmd = Assert.Single(dockerfile.Items.OfType<CmdInstruction>());
        LiteralToken source = copy.SourceTokens[0];
        LiteralToken destination = copy.DestinationToken!;
        Command command = run.Command!;
        Mount cache = Assert.Single(run.Mounts);

        copy.Sources.Remove("obsolete/");
        copy.Sources.Add("generated/");
        copy.Sources.Insert(copy.Sources.IndexOf("generated/"), "LICENSE");
        copy.Sources.Move(copy.Sources.IndexOf("LICENSE"), copy.Sources.Count - 1);
        cache.Entries.Add(new MountEntry("sharing", "locked"));
        expose.Ports[0] = "8080";
        expose.Ports.Add("9090");
        var workdir = new WorkdirInstruction("/app");
        dockerfile.Items.Insert(dockerfile.Items.IndexOf(copy), workdir);

        const string expected =
            "# syntax=docker/dockerfile:1\n" +
            "FROM alpine:3.22\n" +
            "# Application setup\n" +
            "WORKDIR /app\n" +
            "COPY src/ generated/ LICENSE /app/\n" +
            "RUN --mount=type=cache,target=/var/cache/apk,sharing=locked apk add --no-cache curl\n" +
            "EXPOSE 8080 9090\n" +
            "CMD [\"sh\"]\n";
        Assert.Equal(expected, dockerfile.ToString());
        Assert.Equal(new[] { "src/", "generated/", "LICENSE" }, copy.Sources);
        Assert.Equal(new[] { "8080", "9090" }, expose.Ports);
        Assert.Same(source, copy.SourceTokens[0]);
        Assert.Same(destination, copy.DestinationToken);
        Assert.Same(command, run.Command);
        Assert.Same(cache, Assert.Single(run.Mounts));
        Assert.Same(workdir, dockerfile.Items[dockerfile.Items.IndexOf(copy) - 1]);
        Assert.Same(from, Assert.Single(dockerfile.Items.OfType<FromInstruction>()));
        Assert.Same(cmd, Assert.Single(dockerfile.Items.OfType<CmdInstruction>()));
        DockerfileParseResult reparsed = Dockerfile.TryParse(dockerfile.ToString());
        Assert.True(reparsed.Success);
        Assert.Equal(expected, reparsed.Dockerfile!.ToString());
    }

    [Fact]
    public void AnalyzeDependenciesAndUpdateAnExternalCopyImage()
    {
        Dockerfile dockerfile = Dockerfile.Parse(
            "ARG BASE=alpine\n" +
            "FROM ${BASE} AS build\n" +
            "FROM build AS final\n" +
            "COPY --from=busybox /bin/busybox /bin/busybox\n");
        Dictionary<string, string?> overrides = new() { ["BASE"] = "alpine:3.22" };

        DockerfileAnalysis analysis = dockerfile.Analyze(overrides);

        Assert.Same(analysis.GetStage(0), analysis.GetStage("build"));
        DockerfileReference dependency = Assert.Single(analysis.Dependencies);
        Assert.Same(analysis.GetStage("final"), dependency.SourceStage);
        Assert.Same(analysis.GetStage("build"), dependency.TargetStage);
        Assert.Equal(DockerfileReferenceKind.BaseStage, dependency.Kind);
        Assert.Equal(new[] { "alpine:3.22", "busybox" },
            analysis.ExternalImages.Select(image => image.ResolvedValue));
        Assert.Empty(analysis.Diagnostics);

        DockerfileReference copyImage = Assert.Single(analysis.ExternalImages,
            image => image.Kind == DockerfileReferenceKind.CopySource);
        Assert.IsType<CopyInstruction>(copyImage.Instruction).FromStageName = "busybox:1.37";

        Assert.Equal("busybox", copyImage.ResolvedValue);
        Assert.Equal("busybox:1.37", dockerfile.Analyze(overrides).ExternalImages[1].ResolvedValue);
        Assert.Contains("COPY --from=busybox:1.37 ", dockerfile.ToString());
    }

    [Fact]
    public void RecoverAndInspectOriginalSource()
    {
        string text = "FROM scratch\nFUTURE-COPY source target\nFROM\nRUN echo ready\n";
        DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions
        {
            Mode = DockerfileParseMode.Recover,
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        });

        Assert.False(result.Success);
        Dockerfile model = Assert.IsType<Dockerfile>(result.Dockerfile);
        Assert.Equal(text, model.ToString());
        Assert.Single(model.Items.OfType<UnknownInstruction>());
        Assert.Single(model.Items.OfType<MalformedConstruct>());
        RunInstruction run = Assert.Single(model.Items.OfType<RunInstruction>());
        SourceSpan span = Assert.IsType<SourceSpan>(run.SourceSpan);
        Assert.Equal(run.ToString(), text.Substring(span.Start.Offset, span.Length));
        Assert.Collection(result.Diagnostics,
            warning => Assert.Equal(DiagnosticSeverity.Warning, warning.Severity),
            error => Assert.Equal(DiagnosticSeverity.Error, error.Severity));
    }

    [Fact]
    public void StrictTryParseReportsErrorsWithoutReturningAPartialModel()
    {
        DockerfileParseResult result = Dockerfile.TryParse("FROM scratch\nFROM\nRUN echo ready\n");

        Assert.False(result.Success);
        Assert.Null(result.Dockerfile);
        DockerfileDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DockerfileDiagnosticCodes.InvalidSyntax, diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    /// <summary>
    /// The structure of a Dockerfile consists of instructions, whitespace, comments, and parser directives.
    /// </summary>
    [Fact]
    public void DockerfileStructureAndConstructTypes()
    {
        string dockerfileContent = TestHelper.ConcatLines(new List<string>
        {
            "# escape=`",
            "FROM scratch",
            "    ",
            "# TODO"
        });

        // Parse the Dockerfile
        Dockerfile dockerfile = Dockerfile.Parse(dockerfileContent);

        // Verify its structure

        Assert.Equal('`', dockerfile.EscapeChar);

        DockerfileConstruct[] constructs = dockerfile.Items.ToArray();

        Assert.Equal(4, constructs.Length);
            
        Assert.Equal(ConstructType.ParserDirective, constructs[0].Type);
        Assert.IsType<EscapeDirective>(constructs[0]);

        Assert.Equal(ConstructType.Instruction, constructs[1].Type);
        Assert.IsType<FromInstruction>(constructs[1]);

        Assert.Equal(ConstructType.Whitespace, constructs[2].Type);
        Assert.IsType<Whitespace>(constructs[2]);

        Assert.Equal(ConstructType.Comment, constructs[3].Type);
        Assert.IsType<Comment>(constructs[3]);
    }

    /// <summary>
    /// Change the tag of an image name referenced in a FROM instruction.
    /// </summary>
    [Fact]
    public void ChangeTag()
    {
        string dockerfileContent = TestHelper.ConcatLines(new List<string>
        {
            "FROM alpine:3.11",
            "RUN echo \"Hello World\""
        });

        // Parse the Dockerfile
        Dockerfile dockerfile = Dockerfile.Parse(dockerfileContent);
        FromInstruction fromInstruction = dockerfile.Items.OfType<FromInstruction>().First();

        // Parse the image name into its component parts
        ImageName imageName = ImageName.Parse(fromInstruction.ImageName);

        // Change the tag value and set the image name with the new value
        imageName.Tag = "3.12";
        fromInstruction.ImageName = imageName.ToString();

        // Verify the new tag value is output
        string expectedOutput = TestHelper.ConcatLines(new List<string>
        {
            "FROM alpine:3.12",
            "RUN echo \"Hello World\""
        });
        Assert.Equal(expectedOutput, dockerfile.ToString());
    }

    /// <summary>
    /// Resolves both overriden and default ARG values that are referenced throughout a Dockerfile.
    /// </summary>
    [Fact]
    public void ResolveArguments_Globally()
    {
        string dockerfileContent = TestHelper.ConcatLines(new List<string>
        {
            "ARG REPO=alpine",
            "ARG TAG=latest",
            "FROM $REPO:$TAG"
        });

        // Parse the Dockerfile
        Dockerfile dockerfile = Dockerfile.Parse(dockerfileContent);

        // Resolve reference arg values, overriding TAG.
        // This modifies the underlying values of the model, replacing any references to
        // arguments with their resolved values. Be aware of this if your intention is
        // write the model back to the Dockerfile on disk.
        dockerfile.ResolveVariables(
            new Dictionary<string, string?>
            {
                { "TAG", "3.12" }
            },
            options: new ResolutionOptions { UpdateInline = true });

        // Verify the arg values have been resolved
        string expectedOutput = TestHelper.ConcatLines(new List<string>
        {
            "ARG REPO=alpine",
            "ARG TAG=latest",
            "FROM alpine:3.12"
        });
        Assert.Equal(expectedOutput, dockerfile.ToString());
    }

    /// <summary>
    /// Resolves ARG values that are referenced from an instruction without modifying the underlying model.
    /// </summary>
    [Fact]
    public void ResolveArguments_FromValue()
    {
        string dockerfileContent = TestHelper.ConcatLines(new List<string>
        {
            "ARG REPO=alpine",
            "ARG TAG=latest",
            "FROM $REPO:$TAG"
        });

        // Parse the Dockerfile
        Dockerfile dockerfile = Dockerfile.Parse(dockerfileContent);
        FromInstruction fromInstruction = dockerfile.Items.OfType<FromInstruction>().First();

        // Resolve arg values on the specifically on the FROM instruction and have the resolved value returned
        // without modifying the underlying model.
        string resolvedImageName = dockerfile.ResolveVariables(fromInstruction);

        // Verify the image name has the args resolved
        Assert.Equal("FROM alpine:latest", resolvedImageName);
            
        // Verify the underlying value has the arg references maintained
        string expectedOutput = TestHelper.ConcatLines(new List<string>
        {
            "ARG REPO=alpine",
            "ARG TAG=latest",
            "FROM $REPO:$TAG"
        });
        Assert.Equal(expectedOutput, dockerfile.ToString());
    }

    /// <summary>
    /// Each construct within a Dockerfile is made up of tokens of various types. Some tokens
    /// are aggregate tokens that contain other, more primitive, tokens.
    /// </summary>
    [Fact]
    public void Tokens()
    {
        string dockerfileContent = TestHelper.ConcatLines(new List<string>
        {
            "ARG TAG=latest",
            "FROM alpine:$TAG \\",
            "  AS build"
        });

        // Parse the Dockerfile
        Dockerfile dockerfile = Dockerfile.Parse(dockerfileContent);

        DockerfileConstruct[] dockerfileConstructs = dockerfile.Items.ToArray();
            
        // Verify the individual tokens that are contained in the ARG instruction
        Token[] repoArgTokens = dockerfileConstructs[0].Tokens.ToArray();
        Assert.Equal(4, repoArgTokens.Length);
        Assert.IsType<KeywordToken>(repoArgTokens[0]);
        Assert.IsType<WhitespaceToken>(repoArgTokens[1]);
        Assert.IsType<ArgDeclaration>(repoArgTokens[2]);
        Assert.IsType<NewLineToken>(repoArgTokens[3]);

        // Verify the individual tokens that are contained in the FROM instruction
        Token[] fromInstructionTokens = dockerfileConstructs[1].Tokens.ToArray();
        Assert.Equal(9, fromInstructionTokens.Length);
        Assert.IsType<KeywordToken>(fromInstructionTokens[0]);
        Assert.IsType<WhitespaceToken>(fromInstructionTokens[1]);
        Assert.IsType<LiteralToken>(fromInstructionTokens[2]);
        Assert.IsType<WhitespaceToken>(fromInstructionTokens[3]);

        // LineContinuation is an aggregate token that contains other tokens
        Assert.IsType<LineContinuationToken>(fromInstructionTokens[4]);
        LineContinuationToken lineContinuation = (LineContinuationToken)fromInstructionTokens[4];
        Token[] lineContinuationTokens = lineContinuation.Tokens.ToArray();
        Assert.Equal(2, lineContinuationTokens.Length);
        Assert.IsType<SymbolToken>(lineContinuationTokens[0]);
        Assert.IsType<NewLineToken>(lineContinuationTokens[1]);

        Assert.IsType<WhitespaceToken>(fromInstructionTokens[5]);
        Assert.IsType<KeywordToken>(fromInstructionTokens[6]);
        Assert.IsType<WhitespaceToken>(fromInstructionTokens[7]);
        Assert.IsType<StageName>(fromInstructionTokens[8]);
    }

    /// <summary>
    /// Comments can either be top-level or nested within a multi-line instruction.
    /// </summary>
    [Fact]
    public void Comments()
    {
        string dockerfileContent = TestHelper.ConcatLines(new List<string>
        {
            "# top-level comment",
            "FROM alpine \\",
            "  # nested comment",
            "  AS build",
            "# top-level comment",
        });

        // Parse the Dockerfile
        Dockerfile dockerfile = Dockerfile.Parse(dockerfileContent);

        DockerfileConstruct[] dockerfileConstructs = dockerfile.Items.ToArray();
        Assert.Equal(3, dockerfileConstructs.Length);

        Assert.IsType<Comment>(dockerfileConstructs[0]);
            
        // The FROM instruction contains a comment within its context. Any comments that
        // are interspersed amongst the lines of an instruction that spans multiple lines
        // will be contained as comments within the instruction, rather than as top-level
        // comments of the Dockerfile.
        Assert.IsType<FromInstruction>(dockerfileConstructs[1]);
        FromInstruction fromInstruction = (FromInstruction)dockerfileConstructs[1];
        Assert.Single(fromInstruction.Comments);

        Assert.IsType<Comment>(dockerfileConstructs[2]);
    }

    /// <summary>
    /// Create a Dockerfile from scratch using the fluent API of DockerfileBuilder.
    /// </summary>
    [Fact]
    public void CreateNewDockerfileWithDockerfileBuilder()
    {
        DockerfileBuilder builder = new();
        builder
            .Comment("Made from scratch Dockerfile")
            .NewLine()
            .ArgInstruction("TAG", "latest")
            .FromInstruction("alpine:$TAG")
            .ArgInstruction("MESSAGE")
            .RunInstruction("echo $MESSAGE");

        string expectedOutput =
            "# Made from scratch Dockerfile" + Environment.NewLine +
            Environment.NewLine +
            "ARG TAG=latest" + Environment.NewLine +
            "FROM alpine:$TAG" + Environment.NewLine +
            "ARG MESSAGE" + Environment.NewLine +
            "RUN echo $MESSAGE" + Environment.NewLine;

        Assert.Equal(expectedOutput, builder.Dockerfile.ToString());
    }

    /// <summary>
    /// Create a fully-customized Dockerfile from scratch using the fluent API of TokenBuilder.
    /// </summary>
    [Fact]
    public void CreateNewDockerfileWithTokenBuilder()
    {
        DockerfileBuilder builder = new();
        builder
            .Comment("Made from scratch Dockerfile")
            .NewLine()
            .ArgInstruction("TAG", "latest")
            .FromInstruction("alpine:$TAG")
            .ArgInstruction("MESSAGE")
            .RunInstruction(tokenBuilder =>
            {
                // Configure the RUN instruction to have a line continuation with an inline comment
                tokenBuilder
                    .Keyword("RUN")
                    .Whitespace(" ")
                    .LineContinuation()
                    .Whitespace("  ")
                    .Comment(" Output message")
                    .NewLine()
                    .Whitespace("  ")
                    .ShellFormCommand(tokenBuilder =>
                    {
                        tokenBuilder.Literal(tokenBuilder =>
                            tokenBuilder
                                .String("echo ")
                                .VariableRef("MESSAGE"));
                    });
            });

        string expectedOutput =
            "# Made from scratch Dockerfile" + Environment.NewLine +
            Environment.NewLine +
            "ARG TAG=latest" + Environment.NewLine +
            "FROM alpine:$TAG" + Environment.NewLine +
            "ARG MESSAGE" + Environment.NewLine +
            "RUN \\" + Environment.NewLine +
            "  # Output message" + Environment.NewLine +
            "  echo $MESSAGE" + Environment.NewLine;

        Assert.Equal(expectedOutput, builder.Dockerfile.ToString());
    }
}
