using System;
using Xunit;

namespace DockerfileModel.Tests
{
    public class DockerfileBuilderTests
    {
        [Fact]
        public void Constructor()
        {
            DockerfileBuilder builder = new DockerfileBuilder();
            Assert.Equal(String.Empty, builder.Dockerfile.ToString());
            Assert.Equal(String.Empty, builder.ToString());
        }

        [Fact]
        public void BuildAllConstructs()
        {
            DockerfileBuilder builder = new DockerfileBuilder();
            builder
                .ArgInstruction("ARG", "value")
                .CommandInstruction("echo hello")
                .Comment("my comment")
                .FromInstruction("scratch")
                .NewLine()
                .ParserDirective("escape", "\\")
                .RunInstruction("echo hi");

            string expectedOutput =
                "ARG ARG=value" + Environment.NewLine +
                "CMD echo hello" + Environment.NewLine +
                "# my comment" + Environment.NewLine +
                "FROM scratch" + Environment.NewLine +
                Environment.NewLine +
                "# escape=\\" + Environment.NewLine +
                "RUN echo hi" + Environment.NewLine;

            Assert.Equal(expectedOutput, builder.Dockerfile.ToString());
            Assert.Equal(expectedOutput, builder.ToString());
        }

        [Fact]
        public void AutoEscapeDirective_Enabled_Default()
        {
            DockerfileBuilder builder = new DockerfileBuilder();

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
            DockerfileBuilder builder = new DockerfileBuilder
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
            DockerfileBuilder builder = new DockerfileBuilder
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
            DockerfileBuilder builder = new DockerfileBuilder
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
            DockerfileBuilder builder = new DockerfileBuilder
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
            DockerfileBuilder builder = new DockerfileBuilder
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
            DockerfileBuilder builder = new DockerfileBuilder
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
            DockerfileBuilder builder = new DockerfileBuilder
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
            DockerfileBuilder builder = new DockerfileBuilder();
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
}
