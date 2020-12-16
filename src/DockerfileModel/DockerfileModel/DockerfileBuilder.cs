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
            AddConstruct(new Whitespace(DefaultNewLine));

        public DockerfileBuilder AddInstruction(IEnumerable<string> sources, string destination, UserAccount? changeOwnerFlag = null,
            string? permissions = null) =>
            AddConstruct(new AddInstruction(sources, destination, changeOwnerFlag, permissions, EscapeChar));

        public DockerfileBuilder AddInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.AddInstruction.Parse);

        public DockerfileBuilder ArgInstruction(string argName, string? argValue = null) =>
            AddConstruct(new ArgInstruction(argName, argValue, EscapeChar));

        public DockerfileBuilder ArgInstruction(IDictionary<string, string?> args) =>
            AddConstruct(new ArgInstruction(args, EscapeChar));

        public DockerfileBuilder ArgInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.ArgInstruction.Parse);

        public DockerfileBuilder CommandInstruction(string command) =>
            AddConstruct(new CommandInstruction(command, EscapeChar));

        public DockerfileBuilder CommandInstruction(IEnumerable<string> defaultArgs) =>
            AddConstruct(new CommandInstruction(defaultArgs, EscapeChar));

        public DockerfileBuilder CommandInstruction(string command, IEnumerable<string> args) =>
            AddConstruct(new CommandInstruction(command, args, EscapeChar));

        public DockerfileBuilder CommandInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.CommandInstruction.Parse);

        public DockerfileBuilder Comment(string comment) =>
            AddConstruct(new Comment(CommentSeparator + comment));

        public DockerfileBuilder Comment(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.Comment.Parse);

        public DockerfileBuilder CopyInstruction(IEnumerable<string> sources, string destination,
            string? fromStageName = null, UserAccount? changeOwner = null, string? permissions = null) =>
            AddConstruct(new CopyInstruction(sources, destination, fromStageName, changeOwner, permissions, EscapeChar));

        public DockerfileBuilder CopyInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.CopyInstruction.Parse);

        public DockerfileBuilder EntrypointInstruction(string commandWithArgs) =>
            AddConstruct(new EntrypointInstruction(commandWithArgs, EscapeChar));

        public DockerfileBuilder EntrypointInstruction(string command, IEnumerable<string> args) =>
            AddConstruct(new EntrypointInstruction(command, args, EscapeChar));

        public DockerfileBuilder EntrypointInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.EntrypointInstruction.Parse);

        public DockerfileBuilder EnvInstruction(IDictionary<string, string> variables) =>
            AddConstruct(new EnvInstruction(variables, EscapeChar));

        public DockerfileBuilder EnvInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.EnvInstruction.Parse);

        public DockerfileBuilder ExposeInstruction(string port, string? protocol = null) =>
            AddConstruct(new ExposeInstruction(port, protocol, EscapeChar));

        public DockerfileBuilder ExposeInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.ExposeInstruction.Parse);

        public DockerfileBuilder FromInstruction(string imageName, string? stageName = null, string? platform = null) =>
            AddConstruct(new FromInstruction(imageName, stageName, platform, EscapeChar));

        public DockerfileBuilder FromInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.FromInstruction.Parse);

        public DockerfileBuilder GenericInstruction(string instruction, string args) =>
            AddConstruct(new GenericInstruction(instruction, args, EscapeChar));

        public DockerfileBuilder GenericInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.GenericInstruction.Parse);

        public DockerfileBuilder HealthCheckInstruction(string commandWithArgs, string? interval = null, string? timeout = null,
            string? startPeriod = null, string? retries = null) =>
            AddConstruct(new HealthCheckInstruction(commandWithArgs, interval, timeout, startPeriod, retries, EscapeChar));

        public DockerfileBuilder HealthCheckInstruction(IEnumerable<string> defaultArgs, string? interval = null, string? timeout = null,
            string? startPeriod = null, string? retries = null) =>
            AddConstruct(new HealthCheckInstruction(defaultArgs, interval, timeout, startPeriod, retries, EscapeChar));

        public DockerfileBuilder HealthCheckInstruction(string command, IEnumerable<string> args, string? interval = null, string? timeout = null,
            string? startPeriod = null, string? retries = null) =>
            AddConstruct(new HealthCheckInstruction(command, args, interval, timeout, startPeriod, retries, EscapeChar));

        public DockerfileBuilder HealthCheckDisabledInstruction() =>
            AddConstruct(new HealthCheckInstruction(EscapeChar));

        public DockerfileBuilder HealthCheckInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.HealthCheckInstruction.Parse);

        public DockerfileBuilder LabelInstruction(IDictionary<string, string> labels) =>
            AddConstruct(new LabelInstruction(labels, EscapeChar));

        public DockerfileBuilder LabelInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.LabelInstruction.Parse);

        public DockerfileBuilder MaintainerInstruction(string maintainer) =>
            AddConstruct(new MaintainerInstruction(maintainer, EscapeChar));

        public DockerfileBuilder MaintainerInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.MaintainerInstruction.Parse);

        public DockerfileBuilder OnBuildInstruction(Instruction instruction) =>
            AddConstruct(new OnBuildInstruction(instruction, EscapeChar));

        public DockerfileBuilder OnBuildInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.OnBuildInstruction.Parse);

        public DockerfileBuilder ParserDirective(string directive, string value) =>
            AddConstruct(new ParserDirective(CommentSeparator + directive, value));

        public DockerfileBuilder ParserDirective(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.ParserDirective.Parse);

        public DockerfileBuilder RunInstruction(string command) =>
            RunInstruction(command, Enumerable.Empty<Mount>());

        public DockerfileBuilder RunInstruction(string commandWithArgs, IEnumerable<Mount> mounts) =>
            AddConstruct(new RunInstruction(commandWithArgs, mounts, EscapeChar));

        public DockerfileBuilder RunInstruction(string command, IEnumerable<string> args) =>
            RunInstruction(command, args, Enumerable.Empty<Mount>());

        public DockerfileBuilder RunInstruction(string command, IEnumerable<string> args, IEnumerable<Mount> mounts) =>
            AddConstruct(new RunInstruction(command, args, mounts, EscapeChar));

        public DockerfileBuilder RunInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.RunInstruction.Parse);

        public DockerfileBuilder ShellInstruction(string command) =>
            AddConstruct(new ShellInstruction(command, EscapeChar));

        public DockerfileBuilder ShellInstruction(string command, IEnumerable<string> args) =>
            AddConstruct(new ShellInstruction(command, args, EscapeChar));

        public DockerfileBuilder ShellInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.ShellInstruction.Parse);

        public DockerfileBuilder StopSignalInstruction(string signal) =>
           AddConstruct(new StopSignalInstruction(signal, EscapeChar));

        public DockerfileBuilder StopSignalInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.StopSignalInstruction.Parse);

        public DockerfileBuilder UserInstruction(string user, string? group = null) =>
           AddConstruct(new UserInstruction(user, group, EscapeChar));

        public DockerfileBuilder UserInstruction(UserAccount userAccount) =>
           AddConstruct(new UserInstruction(userAccount, EscapeChar));

        public DockerfileBuilder UserInstruction(Action<TokenBuilder> configureBuilder) =>
            ParseTokens(configureBuilder, DockerfileModel.UserInstruction.Parse);

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
                    new ParserDirective(
                        CommentSeparator + DockerfileModel.ParserDirective.EscapeDirective,
                        EscapeChar.ToString()));
                if (!DisableAutoNewLines)
                {
                    Dockerfile.Items.Add(new Whitespace(DefaultNewLine));
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
                Dockerfile.Items.Add(new Whitespace(DefaultNewLine));
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
