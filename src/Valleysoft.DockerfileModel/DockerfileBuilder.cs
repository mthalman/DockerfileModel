using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

/// <summary>Appends typed constructs to a mutable Dockerfile through a fluent API.</summary>
/// <remarks>
/// Methods return this builder and normally append a trailing newline. Set <see cref="DefaultNewLine"/>
/// explicitly for platform-independent output. Token-callback overloads receive a fresh
/// <see cref="TokenBuilder"/> with this builder's escape and newline settings; callbacks supply the entire
/// construct, including its keyword and internal spacing, which is then parsed.
/// </remarks>
public class DockerfileBuilder
{
    /// <summary>Creates a builder owning a new, empty document.</summary>
    public DockerfileBuilder()
        : this(new Dockerfile())
    {
    }

    /// <summary>Creates a builder that appends directly to an existing document.</summary>
    /// <param name="dockerfile">The shared document, not a copy. Its current escape character initializes <see cref="EscapeChar"/>.</param>
    /// <remarks>Existing text is not reformatted, and a missing newline at its end is not repaired before appending.</remarks>
    public DockerfileBuilder(Dockerfile dockerfile)
    {
        Guard.NotNull(dockerfile, nameof(dockerfile));
        Dockerfile = dockerfile;
        EscapeChar = dockerfile.EscapeChar;
    }

    /// <summary>Gets the live document being built; no finalization or copy is required.</summary>
    public Dockerfile Dockerfile { get; }

    /// <summary>Gets or sets the escape context for newly constructed content.</summary>
    /// <remarks>Changing this does not reparse existing items or change their stored editing context.</remarks>
    public char EscapeChar { get; set; }

    /// <summary>Gets or sets generated line endings. Defaults to <see cref="Environment.NewLine"/>.</summary>
    /// <remarks>Use <c>"\n"</c> for LF or <c>"\r\n"</c> for CRLF output.</remarks>
    public string DefaultNewLine { get; set; } = Environment.NewLine;

    /// <summary>Gets or sets whether automatic trailing newlines are disabled. Defaults to false.</summary>
    /// <remarks>Explicit <see cref="NewLine"/> calls still insert a newline.</remarks>
    public bool DisableAutoNewLines { get; set; }

    /// <summary>Gets or sets whether automatic escape-directive insertion is disabled. Defaults to false.</summary>
    /// <remarks>
    /// An automatic header is added only before the first construct of an empty model when
    /// <see cref="EscapeChar"/> is non-default and the construct is not already the matching escape directive.
    /// Existing documents are not given a new header. Adding a conflicting escape directive throws.
    /// </remarks>
    public bool DisableAutoEscapeDirective { get; set; }

    /// <summary>Gets or sets text inserted after <c>#</c> for generated comments and directives. Defaults to one space.</summary>
    public string CommentSeparator { get; set; } = " ";

    /// <summary>Serializes the current document without finalizing or changing it.</summary>
    public override string ToString()
    {
        return Dockerfile.ToString();
    }

    /// <summary>Appends one explicit <see cref="DefaultNewLine"/>, even when automatic newlines are disabled.</summary>
    public DockerfileBuilder NewLine() =>
        AddConstruct(new Whitespace(DefaultNewLine));

    public DockerfileBuilder AddInstruction(IEnumerable<string> sources, string destination, string? changeOwnerFlag = null,
        string? permissions = null, string? checksum = null, bool keepGitDir = false, bool link = false,
        bool unpack = false, IEnumerable<string>? excludes = null) =>
        AddConstruct(new AddInstruction(sources, destination, changeOwner: changeOwnerFlag, permissions: permissions,
            checksum: checksum, keepGitDir: keepGitDir, link: link, unpack: unpack, excludes: excludes, escapeChar: EscapeChar));

    public DockerfileBuilder AddInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.AddInstruction.Parse);

    public DockerfileBuilder ArgInstruction(string argName, string? argValue = null) =>
        AddConstruct(new ArgInstruction(argName, argValue, EscapeChar));

    public DockerfileBuilder ArgInstruction(IDictionary<string, string?> args) =>
        AddConstruct(new ArgInstruction(args, EscapeChar));

    public DockerfileBuilder ArgInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.ArgInstruction.Parse);

    /// <summary>Appends CMD from raw command text, normally shell form; JSON-array text is parsed as exec form.</summary>
    public DockerfileBuilder CmdInstruction(string command) =>
        AddConstruct(new CmdInstruction(command, EscapeChar));

    /// <summary>Appends CMD in JSON exec form using the supplied elements as default arguments.</summary>
    public DockerfileBuilder CmdInstruction(IEnumerable<string> defaultArgs) =>
        AddConstruct(new CmdInstruction(defaultArgs, EscapeChar));

    /// <summary>Appends JSON exec-form CMD with the command as the first element, followed by arguments.</summary>
    public DockerfileBuilder CmdInstruction(string command, IEnumerable<string> args) =>
        AddConstruct(new CmdInstruction(command, args, EscapeChar));

    public DockerfileBuilder CmdInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.CmdInstruction.Parse);

    public DockerfileBuilder Comment(string comment) =>
        AddConstruct(new Comment(CommentSeparator + comment));

    public DockerfileBuilder Comment(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.Comment.Parse);

    public DockerfileBuilder CopyInstruction(IEnumerable<string> sources, string destination,
        string? fromStageName = null, string? changeOwner = null, string? permissions = null,
        bool link = false, bool parents = false, IEnumerable<string>? excludes = null) =>
        AddConstruct(new CopyInstruction(sources, destination, fromStageName, changeOwner, permissions,
            link, parents, excludes, EscapeChar));

    public DockerfileBuilder CopyInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.CopyInstruction.Parse);

    /// <summary>Appends ENTRYPOINT from raw command text, normally shell form; JSON-array text selects exec form.</summary>
    public DockerfileBuilder EntrypointInstruction(string commandWithArgs) =>
        AddConstruct(new EntrypointInstruction(commandWithArgs, EscapeChar));

    /// <summary>Appends JSON exec-form ENTRYPOINT using the supplied elements without shell splitting.</summary>
    public DockerfileBuilder EntrypointInstruction(IEnumerable<string> execArgs) =>
        AddConstruct(new EntrypointInstruction(execArgs, EscapeChar));

    /// <summary>Appends JSON exec-form ENTRYPOINT with the command followed by its arguments.</summary>
    public DockerfileBuilder EntrypointInstruction(string command, IEnumerable<string> args) =>
        AddConstruct(new EntrypointInstruction(command, args, EscapeChar));

    public DockerfileBuilder EntrypointInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.EntrypointInstruction.Parse);

    public DockerfileBuilder EnvInstruction(IDictionary<string, string> variables) =>
        AddConstruct(new EnvInstruction(variables, EscapeChar));

    public DockerfileBuilder EnvInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.EnvInstruction.Parse);

    public DockerfileBuilder ExposeInstruction(string portSpecs) =>
        AddConstruct(new ExposeInstruction(portSpecs, EscapeChar));

    public DockerfileBuilder ExposeInstruction(IEnumerable<string> portSpecs) =>
        AddConstruct(new ExposeInstruction(portSpecs, EscapeChar));

    public DockerfileBuilder ExposeInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.ExposeInstruction.Parse);

    /// <summary>Appends FROM with optional stage alias and platform using the builder's escape context.</summary>
    public DockerfileBuilder FromInstruction(string imageName, string? stageName = null, string? platform = null) =>
        AddConstruct(new FromInstruction(imageName, stageName, platform, EscapeChar));

    /// <summary>Appends FROM by parsing the complete instruction syntax supplied by a token callback.</summary>
    public DockerfileBuilder FromInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.FromInstruction.Parse);

    public DockerfileBuilder GenericInstruction(string instruction, string args) =>
        AddConstruct(new GenericInstruction(instruction, args, EscapeChar));

    public DockerfileBuilder GenericInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.GenericInstruction.Parse);

    /// <summary>Appends HEALTHCHECK CMD from raw command text, normally shell form; JSON-array text selects exec form.</summary>
    public DockerfileBuilder HealthCheckInstruction(string commandWithArgs, string? interval = null, string? timeout = null,
        string? startPeriod = null, string? startInterval = null, string? retries = null) =>
        AddConstruct(new HealthCheckInstruction(commandWithArgs, interval, timeout, startPeriod, startInterval, retries, EscapeChar));

    /// <summary>Appends HEALTHCHECK CMD with the supplied JSON exec-form elements.</summary>
    public DockerfileBuilder HealthCheckInstruction(IEnumerable<string> defaultArgs, string? interval = null, string? timeout = null,
        string? startPeriod = null, string? startInterval = null, string? retries = null) =>
        AddConstruct(new HealthCheckInstruction(defaultArgs, interval, timeout, startPeriod, startInterval, retries, EscapeChar));

    /// <summary>Appends HEALTHCHECK CMD in JSON exec form with the command followed by its arguments.</summary>
    public DockerfileBuilder HealthCheckInstruction(string command, IEnumerable<string> args, string? interval = null, string? timeout = null,
        string? startPeriod = null, string? startInterval = null, string? retries = null) =>
        AddConstruct(new HealthCheckInstruction(command, args, interval, timeout, startPeriod, startInterval, retries, EscapeChar));

    /// <summary>Appends HEALTHCHECK NONE to disable an inherited health check.</summary>
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

    public DockerfileBuilder ParserDirective(string directive, string value)
    {
        Guard.NotNullOrEmpty(directive, nameof(directive));
        Guard.NotNullOrEmpty(value, nameof(value));
        return AddConstruct(DockerfileModel.ParserDirective.Parse($"#{CommentSeparator}{directive}={value}"));
    }

    public DockerfileBuilder ParserDirective(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.ParserDirective.Parse);

    public DockerfileBuilder SyntaxDirective(string value) =>
        ParserDirective(DockerfileModel.ParserDirective.SyntaxDirective, value);

    public DockerfileBuilder SyntaxDirective(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.SyntaxDirective.Parse);

    public DockerfileBuilder EscapeDirective(char escapeChar) =>
        ParserDirective(DockerfileModel.ParserDirective.EscapeDirective,
            new DockerfileModel.EscapeDirective(escapeChar).DirectiveValue);

    public DockerfileBuilder EscapeDirective(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.EscapeDirective.Parse);

    public DockerfileBuilder CheckDirective(string value) =>
        ParserDirective(DockerfileModel.ParserDirective.CheckDirective, value);

    public DockerfileBuilder CheckDirective(CheckDirectiveOptions options) =>
        CheckDirective(new DockerfileModel.CheckDirective(options).DirectiveValue);

    public DockerfileBuilder CheckDirective(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.CheckDirective.Parse);

    /// <summary>Appends RUN from raw command text, normally shell form; JSON-array text selects exec form.</summary>
    public DockerfileBuilder RunInstruction(string command) =>
        RunInstruction(command, Enumerable.Empty<Mount>());

    /// <summary>Appends RUN from raw command text with mount, network, and security flags.</summary>
    public DockerfileBuilder RunInstruction(string commandWithArgs, IEnumerable<Mount> mounts,
        string? network = null, string? security = null) =>
        AddConstruct(new RunInstruction(commandWithArgs, mounts, network, security, EscapeChar));

    /// <summary>Appends JSON exec-form RUN with the command followed by its arguments.</summary>
    public DockerfileBuilder RunInstruction(string command, IEnumerable<string> args) =>
        RunInstruction(command, args, Enumerable.Empty<Mount>());

    /// <summary>Appends JSON exec-form RUN with arguments and mount, network, and security flags.</summary>
    public DockerfileBuilder RunInstruction(string command, IEnumerable<string> args, IEnumerable<Mount> mounts,
        string? network = null, string? security = null) =>
        AddConstruct(new RunInstruction(command, args, mounts, network, security, EscapeChar));

    public DockerfileBuilder RunInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.RunInstruction.Parse);

    /// <summary>Appends SHELL as a one-element JSON array, not as shell-form command text.</summary>
    public DockerfileBuilder ShellInstruction(string command) =>
        AddConstruct(new ShellInstruction(command, EscapeChar));

    /// <summary>Appends SHELL as a JSON array containing the command followed by its arguments.</summary>
    public DockerfileBuilder ShellInstruction(string command, IEnumerable<string> args) =>
        AddConstruct(new ShellInstruction(command, args, EscapeChar));

    public DockerfileBuilder ShellInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.ShellInstruction.Parse);

    public DockerfileBuilder StopSignalInstruction(string signal) =>
        AddConstruct(new StopSignalInstruction(signal, EscapeChar));

    public DockerfileBuilder StopSignalInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.StopSignalInstruction.Parse);

    public DockerfileBuilder UserInstruction(string user) =>
        AddConstruct(new UserInstruction(user, EscapeChar));

    public DockerfileBuilder UserInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.UserInstruction.Parse);

    /// <summary>Appends VOLUME for one path, using JSON form when the path is empty or contains whitespace.</summary>
    public DockerfileBuilder VolumeInstruction(string path) =>
        AddConstruct(new VolumeInstruction(path, EscapeChar));

    /// <summary>Appends VOLUME using JSON form, except for a single nonempty path without whitespace.</summary>
    public DockerfileBuilder VolumeInstruction(IEnumerable<string> paths) =>
        AddConstruct(new VolumeInstruction(paths, EscapeChar));

    public DockerfileBuilder VolumeInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.VolumeInstruction.Parse);

    public DockerfileBuilder WorkdirInstruction(string path) =>
        AddConstruct(new WorkdirInstruction(path, EscapeChar));

    public DockerfileBuilder WorkdirInstruction(Action<TokenBuilder> configureBuilder) =>
        ParseTokens(configureBuilder, DockerfileModel.WorkdirInstruction.Parse);

    private DockerfileBuilder ParseTokens(Action<TokenBuilder> configureBuilder, Func<string, DockerfileConstruct> parseConstruct)
    {
        TokenBuilder builder = new()
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
        TokenBuilder builder = new()
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
            Dockerfile.AddRaw(
                DockerfileModel.ParserDirective.Parse(
                    $"#{CommentSeparator}{DockerfileModel.ParserDirective.EscapeDirective}={EscapeChar}"));
            if (!DisableAutoNewLines)
            {
                Dockerfile.AddRaw(new Whitespace(DefaultNewLine));
            }
        }

        if (IsConflictingEscapeDirective(dockerfileConstruct, out string? escapeDirectiveValue))
        {
            throw new InvalidOperationException(
                $"The escape directive being added, '{escapeDirectiveValue}', conflicts with the escape character set on {nameof(DockerfileBuilder)}: '{EscapeChar}'");
        }

        Dockerfile.AddRaw(dockerfileConstruct);

        if (!DisableAutoNewLines && !(dockerfileConstruct is Whitespace whitespace && whitespace.NewLineToken is not null))
        {
            Dockerfile.AddRaw(new Whitespace(DefaultNewLine));
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
            // No need to auto-add an escape directive when the construct being added is
            // itself an escape directive with the same escape character value. The guard
            // above already returns false when EscapeChar == DefaultEscapeChar, so that
            // sub-condition is unreachable here and has been removed.
            if (parserDirective.HasName(DockerfileModel.ParserDirective.EscapeDirective) &&
                parserDirective.DirectiveValue == EscapeChar.ToString())
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
            parserDirective.HasName(DockerfileModel.ParserDirective.EscapeDirective) &&
            (escapeDirectiveValue = parserDirective.DirectiveValue) != EscapeChar.ToString();
        return isConflicting;
    }
}
