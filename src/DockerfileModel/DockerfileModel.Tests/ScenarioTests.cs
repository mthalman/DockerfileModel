using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Xunit;

namespace DockerfileModel.Tests
{
    public class ScenarioTests
    {
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
            Assert.IsType<ParserDirective>(constructs[0]);

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
                new Dictionary<string, string>
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
            Assert.Equal(6, repoArgTokens.Length);
            Assert.IsType<KeywordToken>(repoArgTokens[0]);
            Assert.IsType<WhitespaceToken>(repoArgTokens[1]);
            Assert.IsType<IdentifierToken>(repoArgTokens[2]);
            Assert.IsType<SymbolToken>(repoArgTokens[3]);
            Assert.IsType<LiteralToken>(repoArgTokens[4]);
            Assert.IsType<NewLineToken>(repoArgTokens[5]);

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
            Assert.IsType<IdentifierToken>(fromInstructionTokens[8]);
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
            DockerfileBuilder builder = new DockerfileBuilder();
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
            DockerfileBuilder builder = new DockerfileBuilder();
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
}
