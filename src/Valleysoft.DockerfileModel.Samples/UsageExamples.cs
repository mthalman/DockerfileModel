using System;
using System.Collections.Generic;
using System.Linq;
using Valleysoft.DockerfileModel;
using Valleysoft.DockerfileModel.Tokens;

using Xunit;

namespace Valleysoft.DockerfileModel.Samples;

public class UsageExamples
{
    /// <summary>
    /// Demonstrates that parsing and serializing an unchanged Dockerfile preserves comments,
    /// extra spacing, CRLF line endings, and the final newline exactly.
    /// </summary>
    [Fact]
    public void RoundTrip()
    {
        // Explicit CRLF makes fidelity independent of this C# file's line endings.
        string text = """
            # Base image
            FROM  alpine:3.22
            RUN echo ready

            """.ReplaceLineEndings("\r\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        string output = dockerfile.ToString();
        Console.WriteLine(output == text); // True, including whitespace and CRLF.

        Assert.Equal(text, output);
    }

    /// <summary>
    /// Demonstrates changing a base image's tag without changing its stage alias,
    /// surrounding whitespace, or subsequent instructions.
    /// </summary>
    [Fact]
    public void UpdateBaseImage()
    {
        string text = """
            FROM  alpine:3.21 AS build
            RUN echo "Hello World"

            """.ReplaceLineEndings("\r\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        FromInstruction from = dockerfile.Items.OfType<FromInstruction>().First();
        // ImageName.Parse creates a separate model; write the edited value back to FROM.
        ImageName image = ImageName.Parse(from.ImageName, dockerfile.EscapeChar);
        image.Tag = "3.22";
        from.ImageName = image.ToString();
        Console.Write(dockerfile);
        // FROM  alpine:3.22 AS build
        // RUN echo "Hello World"

        string expected = """
            FROM  alpine:3.22 AS build
            RUN echo "Hello World"

            """.ReplaceLineEndings("\r\n");
        Assert.Equal(expected, dockerfile.ToString());
    }

    /// <summary>
    /// Demonstrates resolving ARG defaults and overrides without mutation, then opting into
    /// replacing references in the document without rewriting the declared defaults.
    /// </summary>
    [Fact]
    public void ResolveArguments()
    {
        string text = """
            ARG TAG=3.21
            FROM alpine:$TAG
            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        FromInstruction from = dockerfile.Items.OfType<FromInstruction>().Single();
        var overrides = new Dictionary<string, string?> { ["TAG"] = "3.22" };

        // The returned value is the complete instruction, not just its image operand.
        string resolved = dockerfile.ResolveVariables(from, overrides);
        Console.WriteLine(resolved);       // FROM alpine:3.22
        Console.WriteLine(from.ImageName); // alpine:$TAG (model is unchanged)

        // Inline resolution changes references; TAG's declared default remains 3.21.
        dockerfile.ResolveVariables(overrides, new ResolutionOptions { UpdateInline = true });
        Console.WriteLine(dockerfile);
        // ARG TAG=3.21
        // FROM alpine:3.22

        Assert.Equal("FROM alpine:3.22", resolved);
        string expected = """
            ARG TAG=3.21
            FROM alpine:3.22
            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, dockerfile.ToString());

        Dockerfile unmodified = Dockerfile.Parse(text);
        Assert.Equal("FROM alpine:3.22", unmodified.ResolveVariables(
            unmodified.Items.OfType<FromInstruction>().Single(), overrides));
        Assert.Equal(text, unmodified.ToString());
        // Even whole-document resolution leaves the model unchanged unless UpdateInline is set.
        Assert.Equal("FROM alpine:3.22", unmodified.ResolveVariables(overrides));
        Assert.Equal(text, unmodified.ToString());

        string defaultsText = """
            ARG REPO=alpine
            ARG TAG=latest
            FROM $REPO:$TAG
            """.ReplaceLineEndings("\n");
        Dockerfile defaults = Dockerfile.Parse(defaultsText);
        FromInstruction defaultFrom = Assert.Single(defaults.Items.OfType<FromInstruction>());
        Assert.Equal("FROM alpine:latest", defaults.ResolveVariables(defaultFrom));
        Assert.Equal(defaultsText, defaults.ToString());

        // An override for TAG still allows REPO to resolve from its declaration.
        defaults.ResolveVariables(
            new Dictionary<string, string?> { ["TAG"] = "3.12" },
            new ResolutionOptions { UpdateInline = true });
        string overridden = """
            ARG REPO=alpine
            ARG TAG=latest
            FROM alpine:3.12
            """.ReplaceLineEndings("\n");
        Assert.Equal(overridden, defaults.ToString());
    }

    /// <summary>
    /// Demonstrates separating global ARGs and stage contents, and distinguishes captured
    /// stage membership from the shared mutable instruction objects in a stages view.
    /// </summary>
    [Fact]
    public void InspectStages()
    {
        string text = """
            ARG BASE=alpine
            FROM $BASE AS build
            RUN echo ready
            FROM scratch AS final

            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        // This groups constructs; it does not resolve ARGs or analyze stage dependencies.
        StagesView view = new(dockerfile);
        foreach (Stage stage in view.Stages)
        {
            Console.WriteLine($"{stage.Name}: {stage.FromInstruction.ImageName}");
        }
        // build: $BASE
        // final: scratch

        Assert.Single(view.GlobalArgs);
        Assert.Equal(2, view.Stages.Count());
        Stage first = view.Stages.First();
        // A stage exposes its opening FROM separately; the instruction is not a clone.
        Assert.DoesNotContain(first.FromInstruction, first.Items);
        Assert.Same(first.FromInstruction, dockerfile.Items.OfType<FromInstruction>().First());
        first.FromInstruction.StageName = "compile";
        Assert.Equal("compile", first.Name);
        // Structural changes require a fresh view, unlike edits to an existing stage's name.
        dockerfile.Items.Add(new FromInstruction("scratch", "extra"));
        Assert.Equal(2, view.Stages.Count());
        Assert.Equal(3, new StagesView(dockerfile).Stages.Count());
    }

    /// <summary>
    /// Demonstrates deterministic builder output with explicit LF or CRLF, automatic escape
    /// header insertion, exec-form commands, and manual control of trailing newlines.
    /// </summary>
    [Fact]
    public void BuildWithExplicitNewlines()
    {
        // An empty builder adds the header needed for its non-default escape character.
        DockerfileBuilder builder = new() { DefaultNewLine = "\n", EscapeChar = '`' };
        builder.FromInstruction("alpine:3.22")
            .RunInstruction("echo ready")
            .CmdInstruction("echo", new[] { "hello" });
        Console.Write(builder);
        // # escape=`
        // FROM alpine:3.22
        // RUN echo ready
        // CMD ["echo", "hello"]

        string expected = """
            # escape=`
            FROM alpine:3.22
            RUN echo ready
            CMD ["echo", "hello"]

            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, builder.ToString());
        Assert.Equal(expected, Dockerfile.Parse(builder.ToString()).ToString());

        DockerfileBuilder crlf = new() { DefaultNewLine = "\r\n" };
        crlf.FromInstruction("scratch").RunInstruction("echo ready");
        string crlfExpected = """
            FROM scratch
            RUN echo ready

            """.ReplaceLineEndings("\r\n");
        Assert.Equal(crlfExpected, crlf.ToString());

        // Disable automatic separators to emit exactly one boundary and no final newline.
        DockerfileBuilder manual = new() { DefaultNewLine = "\n", DisableAutoNewLines = true };
        manual.FromInstruction("scratch").NewLine().RunInstruction("echo ready");
        string manualExpected = """
            FROM scratch
            RUN echo ready
            """.ReplaceLineEndings("\n");
        Assert.Equal(manualExpected, manual.ToString());
    }

    /// <summary>
    /// Demonstrates using a token callback to control spacing and line continuation syntax,
    /// then inspecting the resulting instruction's nested token model.
    /// </summary>
    [Fact]
    public void UseTokens()
    {
        DockerfileBuilder builder = new() { DefaultNewLine = "\n", EscapeChar = '`' };
        // The callback supplies the full instruction and inherits the builder's escape/newline settings.
        builder.FromInstruction(tokens => tokens
            .Keyword("FROM").Whitespace("  ").Literal("alpine:3.22")
            .Whitespace(" ").LineContinuation().Whitespace("    ")
            .Keyword("AS").Whitespace(" ").StageName("build"));

        FromInstruction from = builder.Dockerfile.Items.OfType<FromInstruction>().Single();
        LineContinuationToken continuation = from.Tokens.OfType<LineContinuationToken>().Single();
        Console.WriteLine(continuation.ToString() == "`\n"); // True
        Console.Write(builder);

        string expected = """
            # escape=`
            FROM  alpine:3.22 `
                AS build

            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, builder.ToString());
        Assert.Equal("`\n", continuation.ToString());
        Assert.Equal(expected, Dockerfile.Parse(builder.ToString()).ToString());
    }

    /// <summary>
    /// Demonstrates editing live instruction and document collections without rebuilding
    /// the COPY instruction or manually inserting its operand separators.
    /// </summary>
    [Fact]
    public void EditCollections()
    {
        string text = """
            FROM alpine AS build
            COPY src/ /app/

            """.ReplaceLineEndings("\n");
        Dockerfile dockerfile = Dockerfile.Parse(text);
        CopyInstruction copy = dockerfile.Items.OfType<CopyInstruction>().Single();
        // These live views update the token model and supply required syntax separators.
        copy.Sources.Add("generated/");
        dockerfile.Items.Insert(dockerfile.Items.IndexOf(copy) + 1, new RunInstruction("echo ready"));

        // Unlike the builder, appending this constructed instruction does not add a final newline.
        string expected = """
            FROM alpine AS build
            COPY src/ generated/ /app/
            RUN echo ready
            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, dockerfile.ToString());
    }
}
