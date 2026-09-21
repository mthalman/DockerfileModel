namespace Valleysoft.DockerfileModel.Tests;

public class DocumentationContractTests
{
    [Fact]
    public void StageArgImportResolvesWorkdirButNotRuntimeCommand()
    {
        string text = """
            ARG ROOT=/global
            FROM alpine
            ARG ROOT
            WORKDIR $ROOT
            RUN echo $ROOT

            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        WorkdirInstruction workdir = dockerfile.Items.OfType<WorkdirInstruction>().Single();
        Assert.Equal("WORKDIR /global\n", dockerfile.ResolveVariables(workdir));
        Assert.Equal(text, dockerfile.ToString());
        Assert.Equal("RUN echo $ROOT\n", dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        string expected = """
            ARG ROOT=/global
            FROM alpine
            ARG ROOT
            WORKDIR /global
            RUN echo $ROOT

            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, dockerfile.ToString());
    }

    [Fact]
    public void CommandConstructorsSelectDocumentedOutputForms()
    {
        Assert.Equal("RUN echo ready", new RunInstruction("echo ready").ToString());
        Assert.Equal("RUN [\"echo\", \"ready\"]", new RunInstruction("[\"echo\", \"ready\"]").ToString());
        Assert.Equal("RUN [\"echo\", \"ready\"]", new RunInstruction("echo", new[] { "ready" }).ToString());
        Assert.Equal("CMD [\"echo\", \"ready\"]", new CmdInstruction("[\"echo\", \"ready\"]").ToString());
        Assert.Equal("CMD [\"echo\", \"ready\"]", new CmdInstruction(new[] { "echo", "ready" }).ToString());
        Assert.Equal("ENTRYPOINT echo ready", new EntrypointInstruction("echo ready").ToString());
        Assert.Equal("ENTRYPOINT [\"echo\", \"ready\"]", new EntrypointInstruction("[\"echo\", \"ready\"]").ToString());
        Assert.Equal("SHELL [\"sh\"]", new ShellInstruction("sh").ToString());
        Assert.Equal("HEALTHCHECK CMD echo ready", new HealthCheckInstruction("echo ready").ToString());
        Assert.Equal("HEALTHCHECK NONE", new HealthCheckInstruction().ToString());
    }

    [Fact]
    public void ConstructedInstructionAcceptsDocumentEscapeContext()
    {
        string text = """
            # escape=`
            FROM alpine

            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        dockerfile.Items.Add(new WorkdirInstruction("/app", dockerfile.EscapeChar));
        string expected = """
            # escape=`
            FROM alpine
            WORKDIR /app
            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, dockerfile.ToString());
    }

    [Fact]
    public void WholeDocumentResolutionReturnsLastInstructionWithoutMutatingByDefault()
    {
        string source = """
            ARG BASE=alpine
            FROM $BASE
            ARG ROOT=/app
            WORKDIR $ROOT
            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(source);

        Assert.Equal("WORKDIR /app", dockerfile.ResolveVariables());
        Assert.Equal(source, dockerfile.ToString());

        Assert.Equal("WORKDIR /app", dockerfile.ResolveVariables(options: new ResolutionOptions { UpdateInline = true }));
        string expected = """
            ARG BASE=alpine
            FROM alpine
            ARG ROOT=/app
            WORKDIR /app
            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, dockerfile.ToString());
        Assert.Equal(string.Empty, Dockerfile.Parse("ARG BASE=alpine").ResolveVariables());
    }

    [Fact]
    public void TargetedInlineResolutionAlsoUpdatesEarlierArgsButNotEarlierNonArgs()
    {
        string source = """
            ARG BASE=alpine
            ARG IMAGE=$BASE
            FROM $IMAGE
            ARG ROOT=/app
            ARG CHILD=$ROOT/bin
            WORKDIR $CHILD
            ARG LATER=$CHILD
            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(source);
        WorkdirInstruction target = Assert.Single(dockerfile.Items.OfType<WorkdirInstruction>());

        Assert.Equal("WORKDIR /app/bin\n", dockerfile.ResolveVariables(target));
        Assert.Equal(source, dockerfile.ToString());

        Assert.Equal("WORKDIR /app/bin\n", dockerfile.ResolveVariables(target,
            options: new ResolutionOptions { UpdateInline = true }));
        string expected = """
            ARG BASE=alpine
            ARG IMAGE=alpine
            FROM $IMAGE
            ARG ROOT=/app
            ARG CHILD=/app/bin
            WORKDIR /app/bin
            ARG LATER=$CHILD
            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, dockerfile.ToString());
    }

    [Fact]
    public void StageGroupingCapturesMembershipButRetainsLiveConstructs()
    {
        string text = """
            FROM alpine AS build
            RUN echo ready
            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        StagesView captured = new(dockerfile);
        Stage stage = Assert.Single(captured.Stages);
        RunInstruction run = Assert.Single(stage.Items.OfType<RunInstruction>());

        Assert.DoesNotContain(stage.FromInstruction, stage.Items);
        stage.FromInstruction.StageName = "renamed";
        Assert.Equal("renamed", stage.Name);
        Assert.Same(run, Assert.Single(dockerfile.Items.OfType<RunInstruction>()));

        new DockerfileBuilder(dockerfile) { DefaultNewLine = "\n" }
            .NewLine()
            .FromInstruction("scratch");

        Assert.Single(captured.Stages);
        Assert.Equal(2, new StagesView(dockerfile).Stages.Count());
    }

    [Fact]
    public void BuilderOnlyInsertsAutomaticEscapeHeaderIntoEmptyDocument()
    {
        DockerfileBuilder empty = new() { EscapeChar = '`', DefaultNewLine = "\n" };
        empty.FromInstruction("scratch");
        string expected = """
            # escape=`
            FROM scratch

            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, empty.ToString());

        string source = """
            FROM scratch

            """.ReplaceLineEndings("\n");
        Dockerfile existing = Dockerfile.Parse(source);
        DockerfileBuilder appended = new(existing) { EscapeChar = '`', DefaultNewLine = "\n" };
        appended.RunInstruction("echo ready");
        Assert.Same(existing, appended.Dockerfile);
        string appendedExpected = """
            FROM scratch
            RUN echo ready

            """.ReplaceLineEndings("\n");
        Assert.Equal(appendedExpected, existing.ToString());
        Assert.Equal('\\', existing.EscapeChar);
    }
}
