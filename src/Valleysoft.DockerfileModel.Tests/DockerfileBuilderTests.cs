namespace Valleysoft.DockerfileModel.Tests;

public class DockerfileBuilderTests
{
    [Fact]
    public void Constructor()
    {
        DockerfileBuilder builder = new();
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
            "VOLUME [\"path\"]" + Environment.NewLine +
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

    [Fact]
    public void DefaultNewLine()
    {
        DockerfileBuilder builder = new()
        {
            DefaultNewLine = "\n"
        };

        string result = builder
            .FromInstruction("scratch")
            .NewLine()
            .ToString();

        string expectedResult =
            "FROM scratch" + "\n" +
            "\n";

        Assert.Equal(expectedResult, result);

        builder = new DockerfileBuilder
        {
            DefaultNewLine = "\r\n"
        };

        result = builder
            .FromInstruction("scratch")
            .NewLine()
            .ToString();

        expectedResult =
            "FROM scratch" + "\r\n" +
            "\r\n";

        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void DisableAutoNewLines()
    {
        DockerfileBuilder builder = new()
        {
                DisableAutoNewLines = true
        };

        string result = builder.FromInstruction("scratch").ToString();
        string expectedResult = "FROM scratch";
        Assert.Equal(expectedResult, result);
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
