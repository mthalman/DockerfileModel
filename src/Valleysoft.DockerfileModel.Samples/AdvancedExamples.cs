using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel.Tokens;
using Xunit;

namespace Valleysoft.DockerfileModel.Samples;

public class AdvancedExamples
{
    /// <summary>
    /// Demonstrates coordinated edits to COPY sources, mount entries, ports, and document
    /// items while preserving unrelated text and the identities of existing model objects.
    /// </summary>
    [Fact]
    public void EditApplicationDockerfileListsWithoutRebuildingInstructions()
    {
        string text = """
            # syntax=docker/dockerfile:1
            FROM alpine:3.22
            # Application setup
            COPY src/ obsolete/ /app/
            RUN --mount=type=cache,target=/var/cache/apk apk add --no-cache curl
            EXPOSE 80
            CMD ["sh"]

            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        CopyInstruction copy = Assert.Single(dockerfile.Items.OfType<CopyInstruction>());
        RunInstruction run = Assert.Single(dockerfile.Items.OfType<RunInstruction>());
        ExposeInstruction expose = Assert.Single(dockerfile.Items.OfType<ExposeInstruction>());
        FromInstruction from = Assert.Single(dockerfile.Items.OfType<FromInstruction>());
        CmdInstruction cmd = Assert.Single(dockerfile.Items.OfType<CmdInstruction>());
        // Retain handles to verify that list edits preserve objects outside the edited elements.
        LiteralToken source = copy.SourceTokens[0];
        LiteralToken destination = copy.DestinationToken!;
        Command command = run.Command!;
        Mount cache = Assert.Single(run.Mounts);

        // Syntax-aware lists maintain operand separators and the mount's CSV syntax.
        copy.Sources.Remove("obsolete/");
        copy.Sources.Add("generated/");
        copy.Sources.Insert(copy.Sources.IndexOf("generated/"), "LICENSE");
        copy.Sources.Move(copy.Sources.IndexOf("LICENSE"), copy.Sources.Count - 1);
        cache.Entries.Add(new MountEntry("sharing", "locked"));
        expose.Ports[0] = "8080";
        expose.Ports.Add("9090");
        var workdir = new WorkdirInstruction("/app");
        dockerfile.Items.Insert(dockerfile.Items.IndexOf(copy), workdir);

        string expected = """
            # syntax=docker/dockerfile:1
            FROM alpine:3.22
            # Application setup
            WORKDIR /app
            COPY src/ generated/ LICENSE /app/
            RUN --mount=type=cache,target=/var/cache/apk,sharing=locked apk add --no-cache curl
            EXPOSE 8080 9090
            CMD ["sh"]

            """.ReplaceLineEndings("\n");
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
        // Reparse the edited text as well as checking it, so token identity is not our only guarantee.
        DockerfileParseResult reparsed = Dockerfile.TryParse(dockerfile.ToString());
        Assert.True(reparsed.Success);
        Assert.Equal(expected, reparsed.Dockerfile!.ToString());
    }

    /// <summary>
    /// Demonstrates distinguishing internal stage dependencies from external images and using
    /// an analysis result's instruction handle to edit an image while retaining snapshot semantics.
    /// </summary>
    [Fact]
    public void AnalyzeDependenciesAndUpdateAnExternalCopyImage()
    {
        string text = """
            ARG BASE=alpine
            FROM ${BASE} AS build
            FROM build AS final
            COPY --from=busybox /bin/busybox /bin/busybox

            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        Dictionary<string, string?> overrides = new() { ["BASE"] = "alpine:3.22" };

        // The override resolves the base image without replacing ${BASE} in the document.
        DockerfileAnalysis analysis = dockerfile.Analyze(overrides);

        Assert.Same(analysis.GetStage(0), analysis.GetStage("build"));
        // Dependency direction is from the consuming stage (final) to its prerequisite (build).
        DockerfileReference dependency = Assert.Single(analysis.Dependencies);
        Assert.Same(analysis.GetStage("final"), dependency.SourceStage);
        Assert.Same(analysis.GetStage("build"), dependency.TargetStage);
        Assert.Equal(DockerfileReferenceKind.BaseStage, dependency.Kind);
        Assert.Equal(new[] { "alpine:3.22", "busybox" },
            analysis.ExternalImages.Select(image => image.ResolvedValue));
        Assert.Empty(analysis.Diagnostics);

        // Analysis values are snapshots, but their instruction handles still refer to the editable model.
        DockerfileReference copyImage = Assert.Single(analysis.ExternalImages,
            image => image.Kind == DockerfileReferenceKind.CopySource);
        Assert.IsType<CopyInstruction>(copyImage.Instruction).FromStageName = "busybox:1.37";

        // Analyze again after an edit rather than expecting the previous result to update.
        Assert.Equal("busybox", copyImage.ResolvedValue);
        Assert.Equal("busybox:1.37", dockerfile.Analyze(overrides).ExternalImages[1].ResolvedValue);
        Assert.Contains("COPY --from=busybox:1.37 ", dockerfile.ToString());
    }

    /// <summary>
    /// Demonstrates opt-in recovery and unknown-instruction preservation, including access
    /// to later valid instructions, original-source spans, and warning/error diagnostics.
    /// </summary>
    [Fact]
    public void RecoverAndInspectOriginalSource()
    {
        string text = """
            FROM scratch
            FUTURE-COPY source target
            FROM
            RUN echo ready

            """.ReplaceLineEndings("\n");
        // Recovery and unknown-instruction preservation are separate opt-ins.
        DockerfileParseResult result = Dockerfile.TryParse(text, new DockerfileParseOptions
        {
            Mode = DockerfileParseMode.Recover,
            UnknownInstructionBehavior = UnknownInstructionBehavior.Preserve
        });

        // An error makes Success false even when recovery returns a lossless, inspectable model.
        Assert.False(result.Success);
        Dockerfile model = Assert.IsType<Dockerfile>(result.Dockerfile);
        Assert.Equal(text, model.ToString());
        Assert.Single(model.Items.OfType<UnknownInstruction>());
        Assert.Single(model.Items.OfType<MalformedConstruct>());
        RunInstruction run = Assert.Single(model.Items.OfType<RunInstruction>());
        // Spans refer to the original input; later model edits would not relocate them.
        SourceSpan span = Assert.IsType<SourceSpan>(run.SourceSpan);
        Assert.Equal(run.ToString(), text.Substring(span.Start.Offset, span.Length));
        Assert.Collection(result.Diagnostics,
            warning => Assert.Equal(DiagnosticSeverity.Warning, warning.Severity),
            error => Assert.Equal(DiagnosticSeverity.Error, error.Severity));
    }

    /// <summary>
    /// Demonstrates the difference between standalone document comments and comments
    /// embedded within a continued instruction.
    /// </summary>
    [Fact]
    public void InspectTopLevelAndNestedComments()
    {
        string text = """
            # top-level comment
            FROM alpine \
              # nested comment
              AS build
            # top-level comment
            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        DockerfileConstruct[] constructs = dockerfile.Items.ToArray();

        // The nested comment belongs to FROM, so it does not add a fourth document item.
        Assert.Equal(3, constructs.Length);
        Assert.IsType<Comment>(constructs[0]);
        FromInstruction from = Assert.IsType<FromInstruction>(constructs[1]);
        Assert.Single(from.Comments);
        Assert.IsType<Comment>(constructs[2]);
    }

    /// <summary>
    /// Demonstrates combining fluent construction with a token callback for a continued
    /// RUN instruction containing an indented comment and a shell-time variable reference.
    /// </summary>
    [Fact]
    public void BuildAContinuedCommandWithANestedComment()
    {
        // The callback controls RUN's internal formatting; the outer builder adds its final newline.
        DockerfileBuilder builder = new();
        builder
            .Comment("Made from scratch Dockerfile")
            .NewLine()
            .ArgInstruction("TAG", "latest")
            .FromInstruction("alpine:$TAG")
            .ArgInstruction("MESSAGE")
            .RunInstruction(tokens => tokens
                .Keyword("RUN")
                .Whitespace(" ")
                .LineContinuation()
                .Whitespace("  ")
                .Comment(" Output message")
                .NewLine()
                .Whitespace("  ")
                // Preserve $MESSAGE in the generated command for the shell to expand at build time.
                .ShellFormCommand(command => command.Literal(literal => literal
                    .String("echo ")
                    .VariableRef("MESSAGE"))));

        string expected = """
            # Made from scratch Dockerfile

            ARG TAG=latest
            FROM alpine:$TAG
            ARG MESSAGE
            RUN \
              # Output message
              echo $MESSAGE

            """.ReplaceLineEndings(Environment.NewLine);
        Assert.Equal(expected, builder.Dockerfile.ToString());
    }
}
