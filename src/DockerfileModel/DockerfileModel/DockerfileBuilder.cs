using System;
using System.Collections.Generic;
using System.Linq;
using DockerfileModel.Tokens;
using Validation;

namespace DockerfileModel
{
    public class DockerfileBuilder
    {
        public DockerfileBuilder()
            : this(new Dockerfile())
        {
        }

        public DockerfileBuilder(Dockerfile dockerfile)
        {
            Requires.NotNull(dockerfile, nameof(dockerfile));
            Dockerfile = dockerfile;
            EscapeChar = dockerfile.EscapeChar;
        }

        public Dockerfile Dockerfile { get; }

        public char EscapeChar { get; set; }

        public string DefaultNewLine { get; set; } = Environment.NewLine;

        public bool DisableAutoNewLines { get; set; }

        public bool DisableAutoEscapeDirective { get; set; }

        public string CommentSeparator { get; set; } = " ";

        public override string ToString()
        {
            return Dockerfile.ToString();
        }

        public DockerfileBuilder NewLine() =>
            AddConstruct(Whitespace.Create(DefaultNewLine));

        public DockerfileBuilder ArgInstruction(string argName, string? argValue = null) =>
            AddConstruct(DockerfileModel.ArgInstruction.Create(argName, argValue, EscapeChar));

        public DockerfileBuilder ArgInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.ArgInstruction.Parse);

        public DockerfileBuilder Comment(string comment) =>
            AddConstruct(
                DockerfileModel.Comment.Create(CommentSeparator + comment));

        public DockerfileBuilder Comment(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.Comment.Parse);

        public DockerfileBuilder FromInstruction(string imageName, string? stageName = null, string? platform = null) =>
            AddConstruct(
                DockerfileModel.FromInstruction.Create(imageName, stageName, platform, EscapeChar));

        public DockerfileBuilder FromInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.FromInstruction.Parse);

        public DockerfileBuilder GenericInstruction(string instruction, string args) =>
            AddConstruct(
                DockerfileModel.GenericInstruction.Create(instruction, args, EscapeChar));

        public DockerfileBuilder GenericInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.GenericInstruction.Parse);

        public DockerfileBuilder ParserDirective(string directive, string value) =>
            AddConstruct(DockerfileModel.ParserDirective.Create(CommentSeparator + directive, value));

        public DockerfileBuilder ParserDirective(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.ParserDirective.Parse);

        public DockerfileBuilder RunInstruction(string command) =>
            RunInstruction(command, Enumerable.Empty<MountFlag>());

        public DockerfileBuilder RunInstruction(string command, IEnumerable<MountFlag> mountFlags) =>
            AddConstruct(DockerfileModel.RunInstruction.Create(command, mountFlags, EscapeChar));

        public DockerfileBuilder RunInstruction(IEnumerable<string> commands) =>
            RunInstruction(commands, Enumerable.Empty<MountFlag>());

        public DockerfileBuilder RunInstruction(IEnumerable<string> commands, IEnumerable<MountFlag> mountFlags) =>
            AddConstruct(DockerfileModel.RunInstruction.Create(commands, mountFlags, EscapeChar));

        public DockerfileBuilder RunInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.RunInstruction.Parse);

        private DockerfileBuilder ParseTokens(Action<TokenBuilder> configureBuilder, Func<string, DockerfileConstruct> parseConstruct)
        {
            TokenBuilder builder = new TokenBuilder
            {
                DefaultNewLine = DefaultNewLine,
                EscapeChar = EscapeChar
            };

            configureBuilder(builder);
            AddConstruct(parseConstruct(builder.ToString()));

            return this;
        }

        private DockerfileBuilder ParseTokens(Action<TokenBuilder> configureBuilder, Func<string, char, DockerfileConstruct> parseConstruct)
        {
            TokenBuilder builder = new TokenBuilder
            {
                DefaultNewLine = DefaultNewLine,
                EscapeChar = EscapeChar
            };

            configureBuilder(builder);
            AddConstruct(parseConstruct(builder.ToString(), EscapeChar));

            return this;
        }

        private DockerfileBuilder AddConstruct(DockerfileConstruct dockerfileConstruct)
        {
            if (CanAutoAddEscapeDirective(dockerfileConstruct))
            {
                Dockerfile.Items.Add(
                    DockerfileModel.ParserDirective.Create(
                        CommentSeparator + DockerfileModel.ParserDirective.EscapeDirective,
                        EscapeChar.ToString()));
                if (!DisableAutoNewLines)
                {
                    Dockerfile.Items.Add(Whitespace.Create(DefaultNewLine));
                }
            }

            if (IsConflictingEscapeDirective(dockerfileConstruct, out string? escapeDirectiveValue))
            {
                throw new InvalidOperationException(
                    $"The escape directive being added, '{escapeDirectiveValue}', conflicts with the escape character set on {nameof(DockerfileBuilder)}: '{EscapeChar}'");
            }

            Dockerfile.Items.Add(dockerfileConstruct);

            if (!DisableAutoNewLines && !(dockerfileConstruct is Whitespace whitespace && whitespace.NewLineToken is not null))
            {
                Dockerfile.Items.Add(Whitespace.Create(DefaultNewLine));
            }

            return this;
        }

        private bool CanAutoAddEscapeDirective(DockerfileConstruct construct)
        {
            if (DisableAutoEscapeDirective || Dockerfile.Items.Any())
            {
                return false;
            }

            if (EscapeChar == Dockerfile.DefaultEscapeChar)
            {
                return false;
            }

            if (construct is ParserDirective parserDirective)
            {
                if (parserDirective.DirectiveName == DockerfileModel.ParserDirective.EscapeDirective &&
                    (EscapeChar == Dockerfile.DefaultEscapeChar ||
                    parserDirective.DirectiveValue == EscapeChar.ToString()))
                {
                    return false;
                }
            }

            return true;
        }
            

        private bool IsConflictingEscapeDirective(DockerfileConstruct construct, out string? escapeDirectiveValue)
        {
            escapeDirectiveValue = null;
            bool isConflicting = construct is ParserDirective parserDirective &&
                parserDirective.DirectiveName == DockerfileModel.ParserDirective.EscapeDirective &&
                (escapeDirectiveValue = parserDirective.DirectiveValue) != EscapeChar.ToString();
            return isConflicting;
        }
    }
}
