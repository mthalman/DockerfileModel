﻿using System;
using System.Collections.Generic;
using System.Linq;
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

        public DockerfileBuilder Comment(string comment) =>
            AddConstruct(
                DockerfileModel.Comment.Create(CommentSeparator + comment));

        public DockerfileBuilder FromInstruction(string imageName, string? stageName = null, string? platform = null) =>
            AddConstruct(
                DockerfileModel.FromInstruction.Create(imageName, stageName, platform, EscapeChar));

        public DockerfileBuilder ParserDirective(string directive, string value) =>
            AddConstruct(DockerfileModel.ParserDirective.Create(CommentSeparator + directive, value));

        public DockerfileBuilder RunInstruction(string command) =>
            AddConstruct(DockerfileModel.RunInstruction.Create(command, EscapeChar));

        public DockerfileBuilder RunInstruction(IEnumerable<string> commands) =>
            AddConstruct(DockerfileModel.RunInstruction.Create(commands, EscapeChar));

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