﻿namespace Valleysoft.DockerfileModel.Tests;

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
