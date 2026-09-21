namespace Valleysoft.DockerfileModel.Tests;

public class DockerfileBuilderTests
{
    [Fact]
    public void BuildWithDefaultNewlines()
    {
        // Comment already gets a trailing newline; NewLine adds the intentional blank line.
        DockerfileBuilder builder = new();
        builder
            .Comment("Made from scratch Dockerfile")
            .NewLine()
            .ArgInstruction("TAG", "latest")
            .FromInstruction("alpine:$TAG")
            .ArgInstruction("MESSAGE")
            .RunInstruction("echo $MESSAGE");

        string expected = """
            # Made from scratch Dockerfile

            ARG TAG=latest
            FROM alpine:$TAG
            ARG MESSAGE
            RUN echo $MESSAGE

            """.ReplaceLineEndings(Environment.NewLine);
        Assert.Equal(expected, builder.Dockerfile.ToString());
    }

    [Fact]
    public void Constructor()
    {
        DockerfileBuilder builder = new();
        Assert.Equal(Environment.NewLine, builder.DefaultNewLine);
        Assert.Equal(String.Empty, builder.Dockerfile.ToString());
        Assert.Equal(String.Empty, builder.ToString());
    }

    [Fact]
    public void BuildAllConstructs()
    {
        DockerfileBuilder builder = new();
        builder
            .AddInstruction(new string[] { "src" }, "dst")
            .ArgInstruction("ARG", "value")
            .CmdInstruction("echo hello")
            .Comment("my comment")
            .CopyInstruction(new string[] { "src" }, "dst")
            .EntrypointInstruction("cmd")
            .EnvInstruction(new Dictionary<string, string>
            {
                { "var1", "val" }
            })
            .ExposeInstruction("80")
            .FromInstruction("scratch")
            .HealthCheckDisabledInstruction()
            .HealthCheckInstruction("cmd")
            .LabelInstruction(new Dictionary<string, string>
            {
                { "label", "val" }
            })
            .MaintainerInstruction("name")
            .NewLine()
            .OnBuildInstruction(new ExposeInstruction("333"))
            .ParserDirective("escape", "\\")
            .RunInstruction("echo hi")
            .ShellInstruction("cmd")
            .StopSignalInstruction("1")
            .UserInstruction("test")
            .VolumeInstruction("path")
            .WorkdirInstruction("path");

        string expectedOutput =
            "ADD src dst" + Environment.NewLine +
            "ARG ARG=value" + Environment.NewLine +
            "CMD echo hello" + Environment.NewLine +
            "# my comment" + Environment.NewLine +
            "COPY src dst" + Environment.NewLine +
            "ENTRYPOINT cmd" + Environment.NewLine +
            "ENV var1=val" + Environment.NewLine +
            "EXPOSE 80" + Environment.NewLine +
            "FROM scratch" + Environment.NewLine +
            "HEALTHCHECK NONE" + Environment.NewLine +
            "HEALTHCHECK CMD cmd" + Environment.NewLine +
            "LABEL label=val" + Environment.NewLine +
            "MAINTAINER name" + Environment.NewLine +
            Environment.NewLine +
            "ONBUILD EXPOSE 333" + Environment.NewLine +
            "# escape=\\" + Environment.NewLine +
            "RUN echo hi" + Environment.NewLine +
            "SHELL [\"cmd\"]" + Environment.NewLine +
            "STOPSIGNAL 1" + Environment.NewLine +
            "USER test" + Environment.NewLine +
            "VOLUME path" + Environment.NewLine +
            "WORKDIR path" + Environment.NewLine;

        Assert.Equal(expectedOutput, builder.Dockerfile.ToString());
        Assert.Equal(expectedOutput, builder.ToString());
    }

    [Fact]
    public void AddInstruction_WithChecksum()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.AddInstruction(new string[] { "https://example.com/file.tar" }, "dst", checksum: "sha256:abc123");
        Assert.Equal("ADD --checksum=sha256:abc123 https://example.com/file.tar dst", builder.ToString());
    }

    [Fact]
    public void AddInstruction_WithKeepGitDir()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.AddInstruction(new string[] { "https://github.com/user/repo.git" }, "dst", keepGitDir: true);
        Assert.Equal("ADD --keep-git-dir https://github.com/user/repo.git dst", builder.ToString());
    }

    [Fact]
    public void AddInstruction_WithLink()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.AddInstruction(new string[] { "src" }, "dst", link: true);
        Assert.Equal("ADD --link src dst", builder.ToString());
    }

    [Fact]
    public void AddInstruction_WithAllNewFlags()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.AddInstruction(new string[] { "https://example.com/file.tar" }, "dst",
            checksum: "sha256:abc123", keepGitDir: true, link: true);
        Assert.Equal("ADD --checksum=sha256:abc123 --keep-git-dir --link https://example.com/file.tar dst", builder.ToString());
    }

    [Fact]
    public void AddInstruction_WithChecksum_AndChown()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.AddInstruction(new string[] { "src" }, "dst", changeOwnerFlag: "myuser", checksum: "sha256:abc123");
        Assert.Equal("ADD --checksum=sha256:abc123 --chown=myuser src dst", builder.ToString());
    }

    [Fact]
    public void AddInstruction_WithUnpack()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.AddInstruction(new string[] { "src.tar" }, "dst", unpack: true);
        Assert.Equal("ADD --unpack src.tar dst", builder.ToString());
    }

    [Fact]
    public void AddInstruction_WithExcludes()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.AddInstruction(new string[] { "src" }, "dst", excludes: new[] { "*.log", "temp" });
        Assert.Equal("ADD --exclude=*.log --exclude=temp src dst", builder.ToString());
    }

    [Fact]
    public void AddInstruction_WithUnpackAndExcludes()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.AddInstruction(new string[] { "src.tar" }, "dst", unpack: true, excludes: new[] { "*.tmp" });
        Assert.Equal("ADD --unpack --exclude=*.tmp src.tar dst", builder.ToString());
    }

    [Fact]
    public void CopyInstruction_WithLink()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        // Build a COPY with --link flag
        builder.CopyInstruction(new string[] { "src" }, "dst", link: true);
        Assert.Equal("COPY --link src dst", builder.ToString());
    }

    [Fact]
    public void CopyInstruction_WithLink_AndFromStageName()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.CopyInstruction(new string[] { "src" }, "dst", fromStageName: "base", link: true);
        Assert.Equal("COPY --from=base --link src dst", builder.ToString());
    }

    [Fact]
    public void CopyInstruction_WithLink_AndChown()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.CopyInstruction(new string[] { "src" }, "dst", changeOwner: "myuser", link: true);
        Assert.Equal("COPY --chown=myuser --link src dst", builder.ToString());
    }

    [Fact]
    public void CopyInstruction_WithLink_AndChmod()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.CopyInstruction(new string[] { "src" }, "dst", permissions: "644", link: true);
        Assert.Equal("COPY --chmod=644 --link src dst", builder.ToString());
    }

    [Fact]
    public void CopyInstruction_WithParents()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.CopyInstruction(new string[] { "src" }, "dst", parents: true);
        Assert.Equal("COPY --parents src dst", builder.ToString());
    }

    [Fact]
    public void CopyInstruction_WithParents_AndLink()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.CopyInstruction(new string[] { "src" }, "dst", link: true, parents: true);
        Assert.Equal("COPY --link --parents src dst", builder.ToString());
    }

    [Fact]
    public void CopyInstruction_WithExclude()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.CopyInstruction(new string[] { "src" }, "dst", excludes: new string[] { "*.txt" });
        Assert.Equal("COPY --exclude=*.txt src dst", builder.ToString());
    }

    [Fact]
    public void CopyInstruction_WithMultipleExcludes()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.CopyInstruction(new string[] { "src" }, "dst", excludes: new string[] { "*.txt", "*.log" });
        Assert.Equal("COPY --exclude=*.txt --exclude=*.log src dst", builder.ToString());
    }

    [Fact]
    public void CopyInstruction_WithAllNewFlags()
    {
        DockerfileBuilder builder = new()
        {
            DisableAutoNewLines = true
        };

        builder.CopyInstruction(new string[] { "src" }, "dst",
            fromStageName: "base", link: true, parents: true, excludes: new string[] { "*.txt" });
        Assert.Equal("COPY --from=base --link --parents --exclude=*.txt src dst", builder.ToString());
    }

    [Fact]
    public void AutoEscapeDirective_Enabled_Default()
    {
        DockerfileBuilder builder = new();

        string result = builder
            .NewLine()
            .FromInstruction("scratch")
            .ToString();

        string expectedResult =
            Environment.NewLine +
            "FROM scratch" + Environment.NewLine;

        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void AutoEscapeDirective_Enabled_NonDefault()
    {
        DockerfileBuilder builder = new()
        {
            EscapeChar = '`'
        };

        string result = builder
            .NewLine()
            .FromInstruction("scratch")
            .ToString();

        string expectedResult =
            "# escape=`" + Environment.NewLine +
            Environment.NewLine +
            "FROM scratch" + Environment.NewLine;

        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void AutoEscapeDirective_Enabled_AddEscapeDirective()
    {
        DockerfileBuilder builder = new()
        {
            EscapeChar = '`'
        };

        string result = builder
            .ParserDirective(ParserDirective.EscapeDirective, "`")
            .ToString();

        string expectedResult = "# escape=`" + Environment.NewLine;

        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void AutoEscapeDirective_Enabled_ConflictingEscapeDirective()
    {
        DockerfileBuilder builder = new()
        {
            EscapeChar = '\\'
        };

        Assert.Throws<InvalidOperationException>(() => builder.ParserDirective(ParserDirective.EscapeDirective, "`"));
        builder.ParserDirective(ParserDirective.EscapeDirective, "\\");

        builder = new DockerfileBuilder
        {
            EscapeChar = '`'
        };
        Assert.Throws<InvalidOperationException>(() => builder.ParserDirective(ParserDirective.EscapeDirective, "\\"));
        builder.ParserDirective(ParserDirective.EscapeDirective, "`");
    }

    [Fact]
    public void AutoEscapeDirective_Disabled()
    {
        DockerfileBuilder builder = new()
        {
            EscapeChar = '`',
            DisableAutoEscapeDirective = true
        };

        string result = builder
            .NewLine()
            .FromInstruction("scratch")
            .ToString();

        string expectedResult =
            Environment.NewLine +
            "FROM scratch" + Environment.NewLine;

        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void CommentSeparator()
    {
        DockerfileBuilder builder = new()
        {
            CommentSeparator = "\t"
        };

        string result = builder
            .ParserDirective(ParserDirective.SyntaxDirective, "test")
            .Comment("test")
            .ToString();

        string expectedResult =
            "#\tsyntax=test" + Environment.NewLine +
            "#\ttest" + Environment.NewLine;

        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void DefaultNewLine(string newline)
    {
        DockerfileBuilder builder = new()
        {
            DefaultNewLine = newline,
            EscapeChar = '`'
        };

        string result = builder
            .FromInstruction("scratch")
            .NewLine()
            .ToString();

        string expectedResult =
            $"# escape=`{newline}FROM scratch{newline}{newline}";

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedResult, Dockerfile.Parse(result).ToString());
    }

    [Theory]
    [InlineData("\n", '\\')]
    [InlineData("\r\n", '\\')]
    [InlineData("\n", '`')]
    [InlineData("\r\n", '`')]
    public void CallbacksInheritDefaultNewLine(string newline, char escapeChar)
    {
        DockerfileBuilder builder = new()
        {
            DefaultNewLine = newline,
            EscapeChar = escapeChar
        };

        string result = builder
            .Comment(tokens =>
            {
                Assert.Equal(newline, tokens.DefaultNewLine);
                tokens.Symbol('#').String(" comment").NewLine();
            })
            .FromInstruction(tokens =>
            {
                Assert.Equal(newline, tokens.DefaultNewLine);
                tokens.Keyword("FROM").Whitespace(" ").LineContinuation()
                    .Whitespace("  ").ImageName("scratch");
            })
            .ToString();

        string directive = escapeChar == '`' ? $"# escape=`{newline}" : "";
        string expected = directive + $"# comment{newline}{newline}FROM {escapeChar}{newline}  scratch{newline}";
        Assert.Equal(expected, result);
        Dockerfile parsed = Dockerfile.Parse(result);
        Assert.Equal(expected, parsed.ToString());
        Assert.Equal("scratch", Assert.Single(parsed.Items.OfType<FromInstruction>()).ImageName);
    }

    [Theory]
    [InlineData("\n", "\r\n")]
    [InlineData("\r\n", "\n")]
    public void AppendingUsesConfiguredNewLineWithoutChangingExistingText(string existingNewline, string newline)
    {
        string original = $"FROM scratch{existingNewline}# retained{existingNewline}";
        DockerfileBuilder builder = new(Dockerfile.Parse(original)) { DefaultNewLine = newline };

        builder.RunInstruction("echo added");

        string expected = original + $"RUN echo added{newline}";
        Assert.Equal(expected, builder.ToString());
        Assert.Equal(expected, Dockerfile.Parse(builder.ToString()).ToString());
    }

    [Fact]
    public void WrappingDocumentDefaultsToHostNewLine()
    {
        string existingNewline = Environment.NewLine == "\n" ? "\r\n" : "\n";
        string original = $"FROM scratch{existingNewline}";
        DockerfileBuilder builder = new(Dockerfile.Parse(original));

        Assert.Equal(Environment.NewLine, builder.DefaultNewLine);
        builder.NewLine().RunInstruction("echo added");

        string expected = original + Environment.NewLine + "RUN echo added" + Environment.NewLine;
        Assert.Equal(expected, builder.ToString());
        Assert.Equal(expected, Dockerfile.Parse(builder.ToString()).ToString());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void DisableAutoNewLines(string newline)
    {
        DockerfileBuilder builder = new()
        {
            DefaultNewLine = newline,
            DisableAutoNewLines = true
        };

        string result = builder.FromInstruction("scratch").ToString();
        string expectedResult = "FROM scratch";
        Assert.Equal(expectedResult, result);
        builder.NewLine().RunInstruction("echo added");
        Assert.Equal($"FROM scratch{newline}RUN echo added", builder.ToString());
    }

    [Fact]
    public void PrebuiltDockerfile()
    {
        DockerfileBuilder builder = new();
        builder.FromInstruction("scratch").ToString();

        builder = new DockerfileBuilder(builder.Dockerfile);
        builder.RunInstruction("echo hello");

        string result = builder.ToString();
            
        string expectedResult =
            "FROM scratch" + Environment.NewLine +
            "RUN echo hello" + Environment.NewLine;
        Assert.Equal(expectedResult, result);
    }
}
