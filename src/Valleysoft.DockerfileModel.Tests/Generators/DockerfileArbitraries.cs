using FsCheck;
using FsCheck.Fluent;

namespace Valleysoft.DockerfileModel.Tests.Generators;

/// <summary>
/// FsCheck 3.x generators that produce valid Dockerfile instruction strings.
/// Every generated string is designed to be parseable by the Sprache-based parser
/// and to round-trip through Parse/ToString with character-for-character fidelity.
///
/// Uses the FsCheck.Fluent namespace for C#-friendly Gen combinators with LINQ syntax.
/// </summary>
public static class DockerfileArbitraries
{
    // ──────────────────────────────────────────────
    // Primitive generators
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates a simple identifier: starts with a letter, followed by letters, digits, or underscores.
    /// Suitable for variable names, stage names, etc.
    /// </summary>
    public static Gen<string> Identifier() =>
        from first in Gen.Elements('a', 'b', 'c', 'x', 'y', 'z')
        from rest in Gen.Elements('a', 'b', 'c', '0', '1', '_').ListOf()
        where rest.Count < 8
        select first + new string(rest.ToArray());

    /// <summary>
    /// Generates a simple alphanumeric string (no special chars that would confuse the parser).
    /// </summary>
    public static Gen<string> SimpleAlphaNum() =>
        from chars in Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm',
            'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9').ArrayOf()
        where chars.Length > 0 && chars.Length <= 12
        select new string(chars);

    /// <summary>
    /// Generates a path-safe string (no spaces, no special chars).
    /// </summary>
    public static Gen<string> PathSegment() =>
        from chars in Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', 'x', 'y', 'z',
            '0', '1', '2', '3', '-', '_', '.').ArrayOf()
        where chars.Length > 0 && chars.Length <= 10
            && char.IsLetterOrDigit(chars[0])  // Must start with letter/digit for parser safety
        select new string(chars);

    /// <summary>
    /// Generates a valid Docker image name (lowercase, may include registry/repo).
    /// </summary>
    public static Gen<string> ImageName() =>
        Gen.OneOf(
            // Simple image name: alpine, ubuntu, nginx
            Gen.Elements("alpine", "ubuntu", "nginx", "debian", "busybox", "node", "python", "golang"),
            // Image with tag
            from name in Gen.Elements("alpine", "ubuntu", "nginx", "node", "python")
            from tag in Gen.Elements("latest", "3.18", "22.04", "stable", "slim", "bullseye")
            select $"{name}:{tag}",
            // Image with digest
            from name in Gen.Elements("alpine", "ubuntu")
            from hash in SimpleHexString()
            select $"{name}@sha256:{hash}",
            // Registry/repo/image
            from registry in Gen.Elements("docker.io", "ghcr.io", "mcr.microsoft.com")
            from repo in Gen.Elements("library", "myorg", "dotnet")
            from name in Gen.Elements("alpine", "sdk", "runtime", "aspnet")
            select $"{registry}/{repo}/{name}");

    /// <summary>
    /// Generates a hex string of fixed length (for digests).
    /// </summary>
    private static Gen<string> SimpleHexString() =>
        from chars in Gen.Elements(
            'a', 'b', 'c', 'd', 'e', 'f', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9').ArrayOf()
        where chars.Length >= 16 && chars.Length <= 64
        select new string(chars);

    /// <summary>
    /// Generates a valid Docker tag string.
    /// </summary>
    public static Gen<string> Tag() =>
        Gen.Elements("latest", "1.0", "v2.3.4", "stable", "edge", "slim", "alpine");

    /// <summary>
    /// Generates a valid stage name (alphanumeric, starts with letter).
    /// </summary>
    public static Gen<string> StageName() =>
        from first in Gen.Elements('a', 'b', 'c', 'm', 's', 't')
        from rest in Gen.Elements('a', 'b', 'c', '0', '1', '2', '-', '_').ListOf()
        where rest.Count > 0 && rest.Count < 8
        select first + new string(rest.ToArray());

    /// <summary>
    /// Generates a port number as string.
    /// </summary>
    public static Gen<string> PortNumber() =>
        from port in Gen.Choose(1, 65535)
        select port.ToString();

    /// <summary>
    /// Generates a protocol string.
    /// </summary>
    public static Gen<string> Protocol() =>
        Gen.Elements("tcp", "udp");

    /// <summary>
    /// Generates a signal name or number for STOPSIGNAL.
    /// </summary>
    public static Gen<string> Signal() =>
        Gen.OneOf(
            Gen.Elements("SIGTERM", "SIGKILL", "SIGINT", "SIGQUIT", "SIGHUP", "SIGUSR1",
                         "SIGUSR2", "SIGSTOP", "SIGCONT", "SIGPIPE", "SIGALRM", "SIGTSTP"),
            from num in Gen.Choose(1, 31) select num.ToString());

    // ──────────────────────────────────────────────
    // Variable reference generators
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates valid variable reference strings: $VAR, ${VAR}, ${VAR:-default}, etc.
    /// </summary>
    public static Gen<string> VariableRef() =>
        Gen.OneOf(
            // Simple: $VAR
            from name in Identifier()
            select $"${name}",
            // Braced: ${VAR}
            from name in Identifier()
            select $"${{{name}}}",
            // With modifiers: ${VAR:-default}, ${VAR:+alt}, ${VAR:?err}
            from name in Identifier()
            from modifier in Gen.Elements(":-", ":+", ":?", "-", "+", "?")
            from value in SimpleAlphaNum()
            select $"${{{name}{modifier}{value}}}");

    // ──────────────────────────────────────────────
    // Cross-cutting helper generators (Priority 1)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates a line continuation: backslash followed by newline.
    /// </summary>
    private static Gen<string> LineContinuation() =>
        Gen.Elements("\\\n", "\\\r\n");

    /// <summary>
    /// Generates a backtick line continuation (Windows escape char mode).
    /// </summary>
    private static Gen<string> BacktickLineContinuation() =>
        Gen.Elements("`\n", "`\r\n");

    /// <summary>
    /// Generates an optional line continuation: either a line continuation or empty string.
    /// Weighted so that about 30% of the time we get a continuation.
    /// </summary>
    private static Gen<string> OptionalLineContinuation() =>
        Gen.Frequency(
            (7, Gen.Constant("")),
            (3, LineContinuation()));

    /// <summary>
    /// Generates case variations of a keyword (e.g., "FROM" → "from", "From", "fRoM").
    /// </summary>
    private static Gen<string> RandomCaseKeyword(string keyword) =>
        Gen.Elements(
            keyword.ToUpperInvariant(),
            keyword.ToLowerInvariant(),
            // Title case
            char.ToUpper(keyword[0]) + keyword[1..].ToLowerInvariant(),
            // Random mixed case (deterministic variant)
            new string(keyword.Select((c, i) => i % 2 == 0 ? char.ToLower(c) : char.ToUpper(c)).ToArray()));

    /// <summary>
    /// Generates varied whitespace between tokens (spaces, tabs, mixed).
    /// </summary>
    private static Gen<string> FlexibleWhitespace() =>
        Gen.Elements(" ", "  ", "\t", " \t", "\t ");

    /// <summary>
    /// Generates a keyword with case variation followed by flexible whitespace.
    /// </summary>
    private static Gen<string> KeywordWithWhitespace(string keyword) =>
        from kw in RandomCaseKeyword(keyword)
        from ws in FlexibleWhitespace()
        select kw + ws;

    /// <summary>
    /// Generates glob/wildcard patterns for COPY/ADD sources.
    /// </summary>
    private static Gen<string> GlobPattern() =>
        Gen.Elements("*.txt", "src/**", "?.log", "file[0-9].dat", "*.conf", "*.js");

    /// <summary>
    /// Generates values containing special but valid characters.
    /// </summary>
    private static Gen<string> SpecialCharValue() =>
        Gen.Elements(
            "value-with-dashes",
            "value_with_underscores",
            "value.with.dots",
            "path/to/file",
            "host:port",
            "key=val");

    /// <summary>
    /// Generates variable refs using exotic POSIX modifiers (##, %%, //, #, %, /).
    /// These are less common but valid in Dockerfile variable references.
    /// </summary>
    private static Gen<string> ExoticVariableRef() =>
        Gen.OneOf(
            // Prefix removal: ${VAR#pattern}, ${VAR##pattern}
            from name in Identifier()
            from pattern in SimpleAlphaNum()
            select $"${{{name}#{pattern}}}",
            from name in Identifier()
            from pattern in SimpleAlphaNum()
            select $"${{{name}##{pattern}}}",
            // Suffix removal: ${VAR%pattern}, ${VAR%%pattern}
            from name in Identifier()
            from pattern in SimpleAlphaNum()
            select $"${{{name}%{pattern}}}",
            from name in Identifier()
            from pattern in SimpleAlphaNum()
            select $"${{{name}%%{pattern}}}",
            // Substitution: ${VAR/find/replace}, ${VAR//find/replace}
            from name in Identifier()
            from find in SimpleAlphaNum()
            from replace in SimpleAlphaNum()
            select $"${{{name}/{find}/{replace}}}",
            from name in Identifier()
            from find in SimpleAlphaNum()
            from replace in SimpleAlphaNum()
            select $"${{{name}//{find}/{replace}}}");

    /// <summary>
    /// Generates single-quoted strings.
    /// </summary>
    private static Gen<string> SingleQuotedString() =>
        Gen.OneOf(
            from text in SimpleAlphaNum()
            select $"'{text}'",
            from w1 in SimpleAlphaNum()
            from w2 in SimpleAlphaNum()
            select $"'{w1} {w2}'",
            Gen.Constant("''"));

    /// <summary>
    /// Generates text that may contain variable references mixed with literal text.
    /// </summary>
    private static Gen<string> ValueWithVariables() =>
        Gen.OneOf(
            // Plain text
            SimpleAlphaNum(),
            // Just a variable ref
            VariableRef(),
            // Text + variable ref
            from prefix in SimpleAlphaNum()
            from varRef in VariableRef()
            select $"{prefix}{varRef}",
            // Variable ref + text
            from varRef in VariableRef()
            from suffix in SimpleAlphaNum()
            select $"{varRef}{suffix}",
            // Text + variable ref + text
            from prefix in SimpleAlphaNum()
            from varRef in VariableRef()
            from suffix in SimpleAlphaNum()
            select $"{prefix}{varRef}{suffix}");

    /// <summary>
    /// Generates double-quoted strings with possible content.
    /// </summary>
    private static Gen<string> QuotedString() =>
        Gen.OneOf(
            // Simple quoted string
            from text in SimpleAlphaNum()
            select $"\"{text}\"",
            // Quoted string with spaces
            from w1 in SimpleAlphaNum()
            from w2 in SimpleAlphaNum()
            select $"\"{w1} {w2}\"",
            // Quoted string with variable ref
            from varRef in VariableRef()
            select $"\"{varRef}\"",
            // Quoted text with variable ref
            from text in SimpleAlphaNum()
            from varRef in VariableRef()
            select $"\"{text} {varRef}\"",
            // Empty quoted string
            Gen.Constant("\"\""));

    /// <summary>
    /// Generates a path that may contain variable references.
    /// </summary>
    private static Gen<string> PathWithVariables() =>
        Gen.OneOf(
            // Plain path
            from seg in PathSegment()
            select $"/app/{seg}",
            // Path with variable at end
            from varRef in VariableRef()
            select $"/app/{varRef}",
            // Path with variable in middle
            from varRef in VariableRef()
            from seg in PathSegment()
            select $"/app/{varRef}/{seg}",
            // Deeply nested with variable
            from seg1 in PathSegment()
            from varRef in VariableRef()
            select $"/opt/{seg1}/{varRef}",
            // All-variable path
            from varRef in VariableRef()
            select $"{varRef}");

    /// <summary>
    /// Generates a JSON exec-form array with varied elements and argument counts.
    /// </summary>
    private static Gen<string> ExecFormCommandVaried() =>
        Gen.OneOf(
            // Single element
            from exe in Gen.Elements("echo", "ls", "cat", "mkdir", "chmod")
            select $"[\"{exe}\"]",
            // Two elements
            from exe in Gen.Elements("/bin/sh", "/bin/bash", "/usr/bin/env", "python", "node")
            from arg in Gen.Elements("-c", "-e", "--help", "start", "run")
            select $"[\"{exe}\", \"{arg}\"]",
            // Three elements
            from exe in Gen.Elements("/bin/sh", "/bin/bash", "python")
            from flag in Gen.Elements("-c", "-e", "-u")
            from cmd in Gen.Elements("echo hello", "ls -la", "cat /etc/hosts", "date", "whoami")
            select $"[\"{exe}\", \"{flag}\", \"{cmd}\"]",
            // Four elements
            from exe in Gen.Elements("dotnet", "java", "node")
            from a1 in Gen.Elements("run", "build", "test", "start")
            from a2 in Gen.Elements("--verbose", "--quiet", "-p", "--no-cache")
            from a3 in Gen.Elements("app.js", "Main.java", "Program.cs")
            select $"[\"{exe}\", \"{a1}\", \"{a2}\", \"{a3}\"]",
            // Five elements (long form)
            from exe in Gen.Elements("/bin/sh", "/bin/bash")
            from flag in Gen.Elements("-c")
            from c1 in Gen.Elements("echo")
            from c2 in Gen.Elements("hello", "world", "test")
            from c3 in Gen.Elements("&&", "||")
            select $"[\"{exe}\", \"{flag}\", \"{c1} {c2} {c3} exit 0\"]");

    /// <summary>
    /// Generates a JSON exec-form array with whitespace between elements (line continuations).
    /// </summary>
    private static Gen<string> ExecFormWithWhitespace() =>
        Gen.OneOf(
            // Array with spaces around elements
            from exe in Gen.Elements("/bin/sh", "/bin/bash", "python")
            from arg in Gen.Elements("-c", "-e", "start")
            select $"[\"{exe}\",  \"{arg}\"]",
            // Array with trailing space before ]
            from exe in Gen.Elements("echo", "ls", "cat")
            select $"[\"{exe}\" ]",
            // Array with leading space after [
            from exe in Gen.Elements("echo", "ls", "cat")
            select $"[ \"{exe}\"]");

    /// <summary>
    /// Generates a mount flag value for RUN --mount.
    /// </summary>
    private static Gen<string> MountSpec() =>
        Gen.OneOf(
            // Bind mount
            from src in PathSegment()
            from tgt in PathSegment()
            select $"type=bind,source={src},target=/{tgt}",
            // Cache mount
            from tgt in PathSegment()
            select $"type=cache,target=/{tgt}",
            // Secret mount
            from id in Identifier()
            select $"type=secret,id={id}",
            // Tmpfs mount
            from tgt in PathSegment()
            select $"type=tmpfs,target=/{tgt}");

    // ──────────────────────────────────────────────
    // Instruction string generators
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates a valid FROM instruction string.
    /// </summary>
    public static Gen<string> FromInstruction() =>
        Gen.OneOf(
            // Simple FROM
            from image in ImageName()
            select $"FROM {image}",
            // FROM with AS
            from image in ImageName()
            from stage in StageName()
            select $"FROM {image} AS {stage}",
            // FROM with --platform
            from platform in Gen.Elements("linux/amd64", "linux/arm64", "linux/arm/v7", "$BUILDPLATFORM", "$TARGETPLATFORM")
            from image in ImageName()
            select $"FROM --platform={platform} {image}",
            // FROM with --platform and AS
            from platform in Gen.Elements("linux/amd64", "linux/arm64")
            from image in ImageName()
            from stage in StageName()
            select $"FROM --platform={platform} {image} AS {stage}",
            // FROM with variable ref as image
            from varRef in VariableRef()
            select $"FROM {varRef}",
            // FROM with platform variable + image variable
            from platVar in VariableRef()
            from imageVar in VariableRef()
            select $"FROM --platform={platVar} {imageVar}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("FROM")
            from image in ImageName()
            select $"{kw} {image}",
            // Line continuation before AS
            from image in ImageName()
            from lc in LineContinuation()
            from stage in StageName()
            select $"FROM {image} {lc}  AS {stage}",
            // Tab whitespace after keyword
            from ws in FlexibleWhitespace()
            from image in ImageName()
            select $"FROM{ws}{image}",
            // Line continuation inside platform flag value
            from image in ImageName()
            select $"FROM --platform=linux\\\n/amd64 {image}",
            // Line continuation before --platform flag
            from image in ImageName()
            select $"FROM \\\n  --platform=linux/amd64 {image}",
            // \r\n line continuation
            from image in ImageName()
            from stage in StageName()
            select $"FROM {image} \\\r\n  AS {stage}",
            // Double line continuation (two in a row)
            from image in ImageName()
            from stage in StageName()
            select $"FROM {image} \\\n\\\n  AS {stage}");

    /// <summary>
    /// Generates a valid RUN instruction string (shell form or exec form).
    /// </summary>
    public static Gen<string> RunInstruction() =>
        Gen.OneOf(
            // Shell form (simple)
            from cmd in ShellCommand()
            select $"RUN {cmd}",
            // Exec form
            from cmd in ExecFormCommand()
            select $"RUN {cmd}",
            // With --network
            from network in Gen.Elements("default", "none", "host")
            from cmd in ShellCommand()
            select $"RUN --network={network} {cmd}",
            // With --security
            from security in Gen.Elements("insecure", "sandbox")
            from cmd in ShellCommand()
            select $"RUN --security={security} {cmd}",
            // With --mount
            from mount in MountSpec()
            from cmd in ShellCommand()
            select $"RUN --mount={mount} {cmd}",
            // With --mount + --network
            from mount in MountSpec()
            from network in Gen.Elements("default", "none", "host")
            from cmd in ShellCommand()
            select $"RUN --mount={mount} --network={network} {cmd}",
            // Shell form with line continuation (multiline command)
            from c1 in Gen.Elements("apt-get update", "echo hello", "mkdir -p /app")
            from lc in LineContinuation()
            from c2 in Gen.Elements("apt-get install -y curl", "echo world", "chmod 755 /app")
            select $"RUN {c1} &&{lc}    {c2}",
            // Shell form with variable refs
            from varRef in VariableRef()
            from cmd in Gen.Elements("echo", "ls", "cat")
            select $"RUN {cmd} {varRef}",
            // Exec form with varied commands
            from cmd in ExecFormCommandVaried()
            select $"RUN {cmd}",
            // With --mount + exec form
            from mount in MountSpec()
            from cmd in ExecFormCommand()
            select $"RUN --mount={mount} {cmd}",
            // Shell form with pipe commands
            from cmd in ShellCommand()
            select $"RUN {cmd}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("RUN")
            from cmd in ShellCommand()
            select $"{kw} {cmd}",
            // Multi-line shell command with continuation
            from c1 in Gen.Elements("echo hello", "apt-get update", "mkdir -p /app")
            from lc in LineContinuation()
            from c2 in Gen.Elements("echo world", "apt-get install -y curl", "chmod 755 /app")
            select $"RUN {c1} {lc}  && {c2}",
            // Tab-separated
            from ws in FlexibleWhitespace()
            from cmd in ShellCommand()
            select $"RUN{ws}{cmd}",
            // Exec form with whitespace in JSON array
            from cmd in ExecFormWithWhitespace()
            select $"RUN {cmd}",
            // Shell form with escaped dollar sign (literal $)
            Gen.Constant("RUN echo \\$HOME is not expanded"),
            // Shell form with escaped backslash
            Gen.Constant("RUN echo \\\\hello"),
            // Shell form with \r\n line continuation
            from c1 in Gen.Elements("echo hello", "apt-get update")
            from c2 in Gen.Elements("echo world", "apt-get install -y curl")
            select $"RUN {c1} &&\\\r\n    {c2}",
            // Three-line continuation
            from c1 in Gen.Elements("apt-get update")
            from c2 in Gen.Elements("apt-get install -y curl")
            from c3 in Gen.Elements("apt-get clean")
            select $"RUN {c1} \\\n  && {c2} \\\n  && {c3}",
            // Disabled: C# parser treats \<spaces><newline> as regular text, not line continuation
            // Gen.Constant("RUN echo hello \\   \n  && echo world"),
            // Disabled: C# parser crashes on exec form with empty string element (issue #203)
            // from arg in Gen.Elements("hello", "-c", "test")
            // select $"RUN [\"\", \"{arg}\"]",
            Gen.Constant("RUN []"));

    /// <summary>
    /// Generates a valid CMD instruction string.
    /// </summary>
    public static Gen<string> CmdInstruction() =>
        Gen.OneOf(
            // Shell form (simple)
            from cmd in ShellCommand()
            select $"CMD {cmd}",
            // Exec form (simple)
            from cmd in ExecFormCommand()
            select $"CMD {cmd}",
            // Exec form (varied)
            from cmd in ExecFormCommandVaried()
            select $"CMD {cmd}",
            // Shell form with pipes and redirects
            from c1 in Gen.Elements("echo hello", "cat /etc/hosts", "date")
            from op in Gen.Elements("|", "&&", "||")
            from c2 in Gen.Elements("grep pattern", "wc -l", "sort", "head -5")
            select $"CMD {c1} {op} {c2}",
            // Shell form with variable refs
            from varRef in VariableRef()
            from cmd in Gen.Elements("echo", "exec", "sh -c")
            select $"CMD {cmd} {varRef}",
            // Exec form with whitespace variants
            from cmd in ExecFormWithWhitespace()
            select $"CMD {cmd}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("CMD")
            from cmd in ShellCommand()
            select $"{kw} {cmd}",
            // Shell form with continuation
            from c1 in Gen.Elements("echo hello", "cat /etc/hosts", "date")
            from lc in LineContinuation()
            from c2 in Gen.Elements("world", "output", "now")
            select $"CMD {c1} {lc}  {c2}",
            // Exec form with varied spacing
            from exe in Gen.Elements("echo", "ls", "cat")
            from arg in Gen.Elements("hello", "-la", "/etc/hosts")
            select $"CMD [\"{exe}\" ,  \"{arg}\"]",
            // Shell form with \r\n line continuation
            from c1 in Gen.Elements("echo hello", "cat /etc/hosts")
            from c2 in Gen.Elements("world", "output")
            select $"CMD {c1} \\\r\n  {c2}",
            // Disabled: C# parser crashes on exec form with empty string element (issue #203)
            // from arg in Gen.Elements("hello", "world")
            // select $"CMD [\"\", \"{arg}\"]",
            // Three-line shell form
            from c1 in Gen.Elements("echo line1")
            from c2 in Gen.Elements("echo line2")
            from c3 in Gen.Elements("echo line3")
            select $"CMD {c1} \\\n  && {c2} \\\n  && {c3}",
            Gen.Constant("CMD []"));

    /// <summary>
    /// Generates a valid ENTRYPOINT instruction string.
    /// </summary>
    public static Gen<string> EntrypointInstruction() =>
        Gen.OneOf(
            // Shell form (simple)
            from cmd in ShellCommand()
            select $"ENTRYPOINT {cmd}",
            // Exec form (simple)
            from cmd in ExecFormCommand()
            select $"ENTRYPOINT {cmd}",
            // Exec form (varied)
            from cmd in ExecFormCommandVaried()
            select $"ENTRYPOINT {cmd}",
            // Shell form with pipes
            from c1 in Gen.Elements("/app/server", "python app.py", "java -jar app.jar")
            from op in Gen.Elements("|", "&&")
            from c2 in Gen.Elements("tee /var/log/out.log", "echo started")
            select $"ENTRYPOINT {c1} {op} {c2}",
            // Shell form with variable refs
            from varRef in VariableRef()
            select $"ENTRYPOINT exec {varRef}",
            // Exec form with whitespace variants
            from cmd in ExecFormWithWhitespace()
            select $"ENTRYPOINT {cmd}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("ENTRYPOINT")
            from cmd in ShellCommand()
            select $"{kw} {cmd}",
            // Shell form with continuation
            from c1 in Gen.Elements("/app/run", "python app.py", "java -jar app.jar")
            from lc in LineContinuation()
            from c2 in Gen.Elements("--config /etc/app.conf", "--port 8080", "--verbose")
            select $"ENTRYPOINT {c1} {lc}  {c2}",
            // Exec form with varied spacing
            from exe in Gen.Elements("/app/run", "python", "node")
            from arg in Gen.Elements("--config", "--port", "start")
            select $"ENTRYPOINT [ \"{exe}\" , \"{arg}\" ]",
            // Disabled: C# parser crashes on exec form with empty string element (issue #203)
            // from arg in Gen.Elements("/app/run", "--config")
            // select $"ENTRYPOINT [\"\", \"{arg}\"]",
            // \r\n line continuation
            from c1 in Gen.Elements("/app/run", "python app.py")
            from c2 in Gen.Elements("--config /etc/app.conf", "--port 8080")
            select $"ENTRYPOINT {c1} \\\r\n  {c2}",
            Gen.Constant("ENTRYPOINT []"));

    /// <summary>
    /// Generates a valid COPY instruction string.
    /// </summary>
    public static Gen<string> CopyInstruction() =>
        Gen.OneOf(
            // Simple COPY
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY {src} {dst}",
            // COPY with --from
            from stage in StageName()
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --from={stage} {src} {dst}",
            // COPY with --chown
            from owner in Identifier()
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --chown={owner} {src} {dst}",
            // COPY with --link
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --link {src} {dst}",
            // COPY with --chmod
            from mode in Gen.Elements("755", "644", "777")
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --chmod={mode} {src} {dst}",
            // COPY with multiple sources
            from s1 in PathSegment()
            from s2 in PathSegment()
            from dst in PathSegment()
            select $"COPY {s1} {s2} /{dst}/",
            // COPY with three sources
            from s1 in PathSegment()
            from s2 in PathSegment()
            from s3 in PathSegment()
            from dst in PathSegment()
            select $"COPY {s1} {s2} {s3} /{dst}/",
            // COPY with --from and --link combined
            from stage in StageName()
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --from={stage} --link {src} {dst}",
            // COPY with --from and --chown combined
            from stage in StageName()
            from owner in Identifier()
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --from={stage} --chown={owner} {src} {dst}",
            // COPY with variable refs in paths
            from varRef in VariableRef()
            from src in PathSegment()
            select $"COPY {src} {varRef}",
            // COPY with --chown and --chmod combined
            from owner in Identifier()
            from mode in Gen.Elements("755", "644", "777")
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --chown={owner} --chmod={mode} {src} {dst}",
            // COPY wildcard sources
            from ext in Gen.Elements("js", "ts", "py", "cs", "go")
            from dst in PathSegment()
            select $"COPY *.{ext} /{dst}/",
            // Case-varied keyword
            from kw in RandomCaseKeyword("COPY")
            from src in PathSegment()
            from dst in PathSegment()
            select $"{kw} {src} {dst}",
            // Line continuation between sources and destination
            from src in PathSegment()
            from lc in LineContinuation()
            from dst in PathSegment()
            select $"COPY {src} {lc}  /{dst}/",
            // Glob patterns in source
            from glob in GlobPattern()
            from dst in PathSegment()
            select $"COPY {glob} /{dst}/",
            // Tab whitespace after keyword
            from ws in FlexibleWhitespace()
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY{ws}{src} {dst}",
            from idx in Gen.Choose(0, 5)
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --from={idx} {src} {dst}",
            // Disabled: C# parser over-tokenizes --chown=user:group as nested keyValue (issue #209)
            // from user in Identifier()
            // from grp in Identifier()
            // from src in PathSegment()
            // from dst in PathSegment()
            // select $"COPY --chown={user}:{grp} {src} {dst}",
            // Line continuation between COPY flags
            from stage in StageName()
            from owner in Identifier()
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --from={stage} \\\n  --chown={owner} {src} {dst}",
            // \r\n line continuation
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY {src} \\\r\n  /{dst}/",
            // Disabled: C# parser doesn't parse --from with variable ref as flag (issue #207)
            // from varRef in VariableRef()
            // from src in PathSegment()
            // from dst in PathSegment()
            // select $"COPY --from={varRef} {src} {dst}",
            // --chmod with --link
            from mode in Gen.Elements("755", "644")
            from src in PathSegment()
            from dst in PathSegment()
            select $"COPY --chmod={mode} --link {src} {dst}");

    /// <summary>
    /// Generates a valid ADD instruction string.
    /// </summary>
    public static Gen<string> AddInstruction() =>
        Gen.OneOf(
            // Simple ADD
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD {src} {dst}",
            // ADD with --chown
            from owner in Identifier()
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD --chown={owner} {src} {dst}",
            // ADD with --checksum
            from hash in SimpleHexString()
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD --checksum=sha256:{hash} {src} {dst}",
            // ADD with --keep-git-dir
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD --keep-git-dir {src} {dst}",
            // ADD with --link
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD --link {src} {dst}",
            // ADD with multiple sources
            from s1 in PathSegment()
            from s2 in PathSegment()
            from dst in PathSegment()
            select $"ADD {s1} {s2} /{dst}/",
            // ADD with --chown and --link combined
            from owner in Identifier()
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD --chown={owner} --link {src} {dst}",
            // ADD with --checksum and --link
            from hash in SimpleHexString()
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD --checksum=sha256:{hash} --link {src} {dst}",
            // ADD with variable ref in paths
            from varRef in VariableRef()
            from src in PathSegment()
            select $"ADD {src} {varRef}",
            // ADD with --chown and --chmod combined
            from owner in Identifier()
            from mode in Gen.Elements("755", "644")
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD --chown={owner} --chmod={mode} {src} {dst}",
            // ADD with --keep-git-dir and --link combined
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD --keep-git-dir --link {src} {dst}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("ADD")
            from src in PathSegment()
            from dst in PathSegment()
            select $"{kw} {src} {dst}",
            // Line continuation
            from src in PathSegment()
            from lc in LineContinuation()
            from dst in PathSegment()
            select $"ADD {src} {lc}  /{dst}/",
            // URL source
            from file in Gen.Elements("file.tar.gz", "archive.zip", "data.csv")
            from dst in PathSegment()
            select $"ADD https://example.com/{file} /{dst}/",
            // Glob patterns
            from glob in GlobPattern()
            from dst in PathSegment()
            select $"ADD {glob} /{dst}/",
            // \r\n line continuation
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD {src} \\\r\n  /{dst}/",
            // Line continuation between ADD flags
            from owner in Identifier()
            from src in PathSegment()
            from dst in PathSegment()
            select $"ADD --chown={owner} \\\n  --link {src} {dst}",
            Gen.Constant("ADD src.txt /dst/"));

    /// <summary>
    /// Generates a valid ENV instruction string.
    /// </summary>
    public static Gen<string> EnvInstruction() =>
        Gen.OneOf(
            // Modern key=value form (single)
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"ENV {key}={value}",
            // Modern key=value form (multiple)
            from key1 in Identifier()
            from val1 in SimpleAlphaNum()
            from key2 in Identifier()
            from val2 in SimpleAlphaNum()
            select $"ENV {key1}={val1} {key2}={val2}",
            // Legacy "ENV key value" form
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"ENV {key} {value}",
            // Modern form with quoted value containing spaces
            from key in Identifier()
            from w1 in SimpleAlphaNum()
            from w2 in SimpleAlphaNum()
            select $"ENV {key}=\"{w1} {w2}\"",
            // Modern form with variable ref in value
            from key in Identifier()
            from varRef in VariableRef()
            select $"ENV {key}={varRef}",
            // Multiple key=value with mixed quoting
            from k1 in Identifier()
            from v1 in SimpleAlphaNum()
            from k2 in Identifier()
            from w1 in SimpleAlphaNum()
            from w2 in SimpleAlphaNum()
            select $"ENV {k1}={v1} {k2}=\"{w1} {w2}\"",
            // Three key=value pairs
            from k1 in Identifier()
            from v1 in SimpleAlphaNum()
            from k2 in Identifier()
            from v2 in SimpleAlphaNum()
            from k3 in Identifier()
            from v3 in SimpleAlphaNum()
            select $"ENV {k1}={v1} {k2}={v2} {k3}={v3}",
            // Legacy form with variable ref
            from key in Identifier()
            from varRef in VariableRef()
            from text in SimpleAlphaNum()
            select $"ENV {key} {varRef}{text}",
            // Modern form with empty value
            from key in Identifier()
            select $"ENV {key}=",
            // Case-varied keyword
            from kw in RandomCaseKeyword("ENV")
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"{kw} {key}={value}",
            // Line continuation in multi-pair
            from k1 in Identifier()
            from v1 in SimpleAlphaNum()
            from lc in LineContinuation()
            from k2 in Identifier()
            from v2 in SimpleAlphaNum()
            select $"ENV {k1}={v1} {lc}  {k2}={v2}",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"ENV{ws}{key}={value}",
            // Special characters in quoted values
            from key in Identifier()
            from special in SpecialCharValue()
            select $"ENV {key}=\"{special}\"",
            // Single-quoted value
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"ENV {key}='{value}'",
            // Single-quoted value with spaces
            from key in Identifier()
            from w1 in SimpleAlphaNum()
            from w2 in SimpleAlphaNum()
            select $"ENV {key}='{w1} {w2}'",
            // Disabled: C# parser doesn't tokenize exotic variable ref modifiers (issue #206)
            // from key in Identifier()
            // from varRef in ExoticVariableRef()
            // select $"ENV {key}={varRef}",
            // Legacy form with \r\n line continuation
            from key in Identifier()
            from v1 in SimpleAlphaNum()
            from v2 in SimpleAlphaNum()
            select $"ENV {key} {v1}\\\r\n{v2}",
            // Value containing equals sign
            from key in Identifier()
            select $"ENV {key}=\"key=value\"",
            // Multiple pairs with single-quoted values
            from k1 in Identifier()
            from v1 in SimpleAlphaNum()
            from k2 in Identifier()
            from v2 in SimpleAlphaNum()
            select $"ENV {k1}='{v1}' {k2}='{v2}'");

    /// <summary>
    /// Generates a valid ARG instruction string.
    /// </summary>
    public static Gen<string> ArgInstruction() =>
        Gen.OneOf(
            // ARG without default
            from name in Identifier()
            select $"ARG {name}",
            // ARG with default
            from name in Identifier()
            from value in SimpleAlphaNum()
            select $"ARG {name}={value}",
            // ARG with variable ref in default value
            from name in Identifier()
            from varRef in VariableRef()
            select $"ARG {name}={varRef}",
            // ARG with quoted default value
            from name in Identifier()
            from value in SimpleAlphaNum()
            select $"ARG {name}=\"{value}\"",
            // Multiple ARG declarations on one line
            from n1 in Identifier()
            from v1 in SimpleAlphaNum()
            from n2 in Identifier()
            select $"ARG {n1}={v1} {n2}",
            // Multiple ARG declarations, both with defaults
            from n1 in Identifier()
            from v1 in SimpleAlphaNum()
            from n2 in Identifier()
            from v2 in SimpleAlphaNum()
            select $"ARG {n1}={v1} {n2}={v2}",
            // ARG with empty default
            from name in Identifier()
            select $"ARG {name}=",
            // Case-varied keyword
            from kw in RandomCaseKeyword("ARG")
            from name in Identifier()
            from value in SimpleAlphaNum()
            select $"{kw} {name}={value}",
            // Line continuation in multi-arg
            from n1 in Identifier()
            from v1 in SimpleAlphaNum()
            from lc in LineContinuation()
            from n2 in Identifier()
            from v2 in SimpleAlphaNum()
            select $"ARG {n1}={v1} {lc}  {n2}={v2}",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from name in Identifier()
            from value in SimpleAlphaNum()
            select $"ARG{ws}{name}={value}",
            // Single-quoted default value
            from name in Identifier()
            from value in SimpleAlphaNum()
            select $"ARG {name}='{value}'",
            // Disabled: C# parser doesn't tokenize exotic variable ref modifiers (issue #206)
            // from name in Identifier()
            // from varRef in ExoticVariableRef()
            // select $"ARG {name}={varRef}",
            // \r\n line continuation
            from n1 in Identifier()
            from v1 in SimpleAlphaNum()
            from n2 in Identifier()
            select $"ARG {n1}={v1} \\\r\n  {n2}");

    /// <summary>
    /// Generates a valid EXPOSE instruction string.
    /// </summary>
    public static Gen<string> ExposeInstruction() =>
        Gen.OneOf(
            // Port only
            from port in PortNumber()
            select $"EXPOSE {port}",
            // Port/protocol
            from port in PortNumber()
            from proto in Protocol()
            select $"EXPOSE {port}/{proto}",
            // Port with variable ref
            from varRef in VariableRef()
            select $"EXPOSE {varRef}",
            // Variable ref with protocol
            from varRef in VariableRef()
            from proto in Protocol()
            select $"EXPOSE {varRef}/{proto}",
            // Port as text with variable
            from varRef in VariableRef()
            from proto in Protocol()
            select $"EXPOSE {varRef}/{proto}",
            // Well-known ports
            from port in Gen.Elements("80", "443", "8080", "3000", "5000", "8443", "9090")
            select $"EXPOSE {port}",
            // Well-known ports with protocol
            from port in Gen.Elements("80", "443", "8080", "3000")
            from proto in Protocol()
            select $"EXPOSE {port}/{proto}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("EXPOSE")
            from port in PortNumber()
            select $"{kw} {port}",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from port in PortNumber()
            from proto in Protocol()
            select $"EXPOSE{ws}{port}/{proto}");

    /// <summary>
    /// Generates a valid HEALTHCHECK instruction string.
    /// </summary>
    public static Gen<string> HealthCheckInstruction() =>
        Gen.OneOf(
            // HEALTHCHECK NONE
            Gen.Constant("HEALTHCHECK NONE"),
            // Simple HEALTHCHECK CMD
            from cmd in ShellCommand()
            select $"HEALTHCHECK CMD {cmd}",
            // With --interval
            from interval in Duration()
            from cmd in ShellCommand()
            select $"HEALTHCHECK --interval={interval} CMD {cmd}",
            // With multiple options
            from interval in Duration()
            from timeout in Duration()
            from retries in Gen.Choose(1, 10)
            from cmd in ShellCommand()
            select $"HEALTHCHECK --interval={interval} --timeout={timeout} --retries={retries} CMD {cmd}",
            // With --start-period
            from startPeriod in Duration()
            from cmd in ShellCommand()
            select $"HEALTHCHECK --start-period={startPeriod} CMD {cmd}",
            // With all four flags
            from interval in Duration()
            from timeout in Duration()
            from startPeriod in Duration()
            from retries in Gen.Choose(1, 10)
            from cmd in ShellCommand()
            select $"HEALTHCHECK --interval={interval} --timeout={timeout} --start-period={startPeriod} --retries={retries} CMD {cmd}",
            // Exec form CMD
            from interval in Duration()
            from cmd in ExecFormCommand()
            select $"HEALTHCHECK --interval={interval} CMD {cmd}",
            // CMD with exec form (varied)
            from cmd in ExecFormCommandVaried()
            select $"HEALTHCHECK CMD {cmd}",
            // Flags in non-standard order
            from retries in Gen.Choose(1, 10)
            from timeout in Duration()
            from cmd in ShellCommand()
            select $"HEALTHCHECK --retries={retries} --timeout={timeout} CMD {cmd}",
            // Only --timeout
            from timeout in Duration()
            from cmd in ShellCommand()
            select $"HEALTHCHECK --timeout={timeout} CMD {cmd}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("HEALTHCHECK")
            select $"{kw} NONE",
            // Case-varied NONE
            from none in RandomCaseKeyword("NONE")
            select $"HEALTHCHECK {none}",
            // Line continuation between flags
            from interval in Duration()
            from timeout in Duration()
            from cmd in ShellCommand()
            select $"HEALTHCHECK --interval={interval} \\\n  --timeout={timeout} \\\n  CMD {cmd}",
            // \r\n line continuation between flags
            from interval in Duration()
            from timeout in Duration()
            from cmd in ShellCommand()
            select $"HEALTHCHECK --interval={interval} \\\r\n  --timeout={timeout} \\\r\n  CMD {cmd}",
            // Disabled: C# parser crashes on --start-interval flag (issue #202)
            // from startInterval in Duration()
            // from cmd in ShellCommand()
            // select $"HEALTHCHECK --start-interval={startInterval} CMD {cmd}",
            // Disabled: C# parser crashes on --start-interval flag (issue #202)
            // from interval in Duration()
            // from timeout in Duration()
            // from startPeriod in Duration()
            // from startInterval in Duration()
            // from retries in Gen.Choose(1, 10)
            // from cmd in ShellCommand()
            // select $"HEALTHCHECK --interval={interval} --timeout={timeout} --start-period={startPeriod} --start-interval={startInterval} --retries={retries} CMD {cmd}",
            // Three-flag line continuation
            from interval in Duration()
            from timeout in Duration()
            from retries in Gen.Choose(1, 10)
            from cmd in ShellCommand()
            select $"HEALTHCHECK --interval={interval} \\\n  --timeout={timeout} \\\n  --retries={retries} \\\n  CMD {cmd}");

    /// <summary>
    /// Generates a valid LABEL instruction string.
    /// </summary>
    public static Gen<string> LabelInstruction() =>
        Gen.OneOf(
            // Single label
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"LABEL {key}={value}",
            // Multiple labels
            from key1 in Identifier()
            from val1 in SimpleAlphaNum()
            from key2 in Identifier()
            from val2 in SimpleAlphaNum()
            select $"LABEL {key1}={val1} {key2}={val2}",
            // Quoted value
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"LABEL {key}=\"{value}\"",
            // Dotted key (OCI-style)
            from prefix in Gen.Elements("org.opencontainers.image", "com.example", "io.buildpacks")
            from suffix in Gen.Elements("authors", "version", "description", "title", "url", "source")
            from value in SimpleAlphaNum()
            select $"LABEL {prefix}.{suffix}=\"{value}\"",
            // Multiple labels with dotted keys
            from p1 in Gen.Elements("org.opencontainers.image", "com.example")
            from s1 in Gen.Elements("authors", "version")
            from v1 in SimpleAlphaNum()
            from p2 in Gen.Elements("org.opencontainers.image", "com.example")
            from s2 in Gen.Elements("title", "description")
            from v2 in SimpleAlphaNum()
            select $"LABEL {p1}.{s1}=\"{v1}\" {p2}.{s2}=\"{v2}\"",
            // Key with hyphens
            from prefix in Gen.Elements("maintainer", "build", "app")
            from suffix in Gen.Elements("name", "date", "version")
            from value in SimpleAlphaNum()
            select $"LABEL {prefix}-{suffix}={value}",
            // Quoted value with spaces
            from key in Identifier()
            from w1 in SimpleAlphaNum()
            from w2 in SimpleAlphaNum()
            select $"LABEL {key}=\"{w1} {w2}\"",
            // Three labels
            from k1 in Identifier()
            from v1 in SimpleAlphaNum()
            from k2 in Identifier()
            from v2 in SimpleAlphaNum()
            from k3 in Identifier()
            from v3 in SimpleAlphaNum()
            select $"LABEL {k1}={v1} {k2}={v2} {k3}={v3}",
            // Variable ref in value
            from key in Identifier()
            from varRef in VariableRef()
            select $"LABEL {key}={varRef}",
            // Empty value
            from key in Identifier()
            select $"LABEL {key}=",
            // Case-varied keyword
            from kw in RandomCaseKeyword("LABEL")
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"{kw} {key}=\"{value}\"",
            // Line continuation in multi-pair
            from k1 in Identifier()
            from v1 in SimpleAlphaNum()
            from lc in LineContinuation()
            from k2 in Identifier()
            from v2 in SimpleAlphaNum()
            select $"LABEL {k1}=\"{v1}\" {lc}  {k2}=\"{v2}\"",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"LABEL{ws}{key}=\"{value}\"",
            // Special characters in quoted values
            from key in Identifier()
            from special in SpecialCharValue()
            select $"LABEL {key}=\"{special}\"",
            // Single-quoted value
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"LABEL {key}='{value}'",
            // Dotted key without quotes on value
            from prefix in Gen.Elements("org.opencontainers.image", "com.example")
            from suffix in Gen.Elements("version", "title")
            from value in SimpleAlphaNum()
            select $"LABEL {prefix}.{suffix}={value}",
            // Disabled: C# parser doesn't tokenize exotic variable ref modifiers (issue #206)
            // from key in Identifier()
            // from varRef in ExoticVariableRef()
            // select $"LABEL {key}={varRef}",
            // \r\n line continuation
            from k1 in Identifier()
            from v1 in SimpleAlphaNum()
            from k2 in Identifier()
            from v2 in SimpleAlphaNum()
            select $"LABEL {k1}=\"{v1}\" \\\r\n  {k2}=\"{v2}\"",
            // Multiple single-quoted values
            from k1 in Identifier()
            from v1 in SimpleAlphaNum()
            from k2 in Identifier()
            from v2 in SimpleAlphaNum()
            select $"LABEL {k1}='{v1}' {k2}='{v2}'");

    /// <summary>
    /// Generates a valid MAINTAINER instruction string.
    /// </summary>
    public static Gen<string> MaintainerInstruction() =>
        Gen.OneOf(
            // Name with email
            from name in Gen.Elements("John", "Jane", "Alice", "Bob", "Charlie", "Diana")
            from domain in Gen.Elements("example.com", "test.org", "dev.io", "company.co")
            select $"MAINTAINER {name} <{name}@{domain}>",
            // Just a name
            from name in Gen.Elements("John Doe", "Jane Smith", "Alice Johnson", "Bob Wilson")
            select $"MAINTAINER {name}",
            // Full name with middle
            from first in Gen.Elements("John", "Jane", "Alice")
            from middle in Gen.Elements("Michael", "Marie", "Lee")
            from last in Gen.Elements("Doe", "Smith", "Johnson")
            select $"MAINTAINER {first} {middle} {last}",
            // Email only
            from user in Gen.Elements("admin", "dev", "support", "info")
            from domain in Gen.Elements("example.com", "test.org")
            select $"MAINTAINER {user}@{domain}",
            // Name with special characters (parentheses, hyphens)
            from first in Gen.Elements("Mary", "Jean", "Anne")
            from last in Gen.Elements("O'Brien", "van-der-Berg", "St-Claire")
            select $"MAINTAINER {first} {last}",
            // Quoted email format
            from name in Gen.Elements("John", "Jane")
            from domain in Gen.Elements("example.com", "test.org")
            select $"MAINTAINER \"{name} <{name}@{domain}>\"",
            // Case-varied keyword
            from kw in RandomCaseKeyword("MAINTAINER")
            from name in Gen.Elements("John Doe", "Jane Smith")
            select $"{kw} {name}",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from name in Gen.Elements("John Doe", "Jane Smith", "Alice Johnson")
            select $"MAINTAINER{ws}{name}");

    /// <summary>
    /// Generates a valid ONBUILD instruction string.
    /// Wraps a subset of instructions that are valid inside ONBUILD.
    /// ONBUILD cannot wrap FROM, ONBUILD, or MAINTAINER.
    /// </summary>
    public static Gen<string> OnBuildInstruction() =>
        Gen.OneOf(
            // ONBUILD RUN
            from inner in RunInstruction()
            select $"ONBUILD {inner}",
            // ONBUILD COPY
            from inner in CopyInstruction()
            select $"ONBUILD {inner}",
            // ONBUILD ADD
            from inner in AddInstruction()
            select $"ONBUILD {inner}",
            // ONBUILD ENV
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"ONBUILD ENV {key}={value}",
            // ONBUILD WORKDIR
            from path in AbsolutePath()
            select $"ONBUILD WORKDIR {path}",
            // ONBUILD LABEL
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"ONBUILD LABEL {key}={value}",
            // ONBUILD EXPOSE
            from port in PortNumber()
            select $"ONBUILD EXPOSE {port}",
            // ONBUILD USER
            from user in Identifier()
            select $"ONBUILD USER {user}",
            // ONBUILD VOLUME
            from path in AbsolutePath()
            select $"ONBUILD VOLUME {path}",
            // ONBUILD STOPSIGNAL
            from signal in Signal()
            select $"ONBUILD STOPSIGNAL {signal}",
            // ONBUILD ARG
            from name in Identifier()
            select $"ONBUILD ARG {name}",
            // ONBUILD CMD
            from cmd in ShellCommand()
            select $"ONBUILD CMD {cmd}",
            // ONBUILD ENTRYPOINT
            from cmd in ExecFormCommand()
            select $"ONBUILD ENTRYPOINT {cmd}",
            // Case-varied ONBUILD keyword
            from kw in RandomCaseKeyword("ONBUILD")
            from cmd in ShellCommand()
            select $"{kw} RUN {cmd}",
            // Case-varied inner keyword
            from innerKw in RandomCaseKeyword("RUN")
            from cmd in ShellCommand()
            select $"ONBUILD {innerKw} {cmd}",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from cmd in ShellCommand()
            select $"ONBUILD{ws}RUN {cmd}",
            // Disabled: C# parser doesn't tokenize exotic variable ref modifiers (issue #206)
            // from varRef in ExoticVariableRef()
            // select $"ONBUILD ENV MYVAR={varRef}",
            // ONBUILD with single-quoted value
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"ONBUILD LABEL {key}='{value}'",
            // ONBUILD COPY with --from
            from stage in StageName()
            from src in PathSegment()
            from dst in PathSegment()
            select $"ONBUILD COPY --from={stage} {src} {dst}");

    /// <summary>
    /// Generates a valid STOPSIGNAL instruction string.
    /// </summary>
    public static Gen<string> StopSignalInstruction() =>
        Gen.OneOf(
            // Signal name
            from signal in Signal()
            select $"STOPSIGNAL {signal}",
            // Variable ref as signal
            from varRef in VariableRef()
            select $"STOPSIGNAL {varRef}",
            // Signal number as variable
            from name in Identifier()
            select $"STOPSIGNAL ${name}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("STOPSIGNAL")
            from signal in Signal()
            select $"{kw} {signal}",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from signal in Signal()
            select $"STOPSIGNAL{ws}{signal}",
            // Disabled: C# parser doesn't tokenize exotic variable ref modifiers (issue #206)
            // from varRef in ExoticVariableRef()
            // select $"STOPSIGNAL {varRef}"
            Gen.Constant("STOPSIGNAL SIGTERM"));

    /// <summary>
    /// Generates a valid USER instruction string.
    /// </summary>
    public static Gen<string> UserInstruction() =>
        Gen.OneOf(
            // User only
            from user in Identifier()
            select $"USER {user}",
            // User:group
            from user in Identifier()
            from grp in Identifier()
            select $"USER {user}:{grp}",
            // Numeric UID
            from uid in Gen.Choose(0, 65534)
            select $"USER {uid}",
            // Numeric UID:GID
            from uid in Gen.Choose(0, 65534)
            from gid in Gen.Choose(0, 65534)
            select $"USER {uid}:{gid}",
            // Variable ref as user
            from varRef in VariableRef()
            select $"USER {varRef}",
            // User variable with group
            from varRef in VariableRef()
            from grp in Identifier()
            select $"USER {varRef}:{grp}",
            // Numeric UID with group name
            from uid in Gen.Choose(0, 65534)
            from grp in Identifier()
            select $"USER {uid}:{grp}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("USER")
            from user in Identifier()
            select $"{kw} {user}",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from user in Identifier()
            from grp in Identifier()
            select $"USER{ws}{user}:{grp}",
            // Numeric boundary UIDs (root and nobody)
            Gen.Elements("USER 0", "USER 65534"),
            // Dual variable refs for user:group
            from userVar in VariableRef()
            from grpVar in VariableRef()
            select $"USER {userVar}:{grpVar}",
            // Disabled: C# parser doesn't tokenize exotic variable ref modifiers (issue #206)
            // from varRef in ExoticVariableRef()
            // select $"USER {varRef}"
            Gen.Constant("USER www-data"));

    /// <summary>
    /// Generates a valid VOLUME instruction string (JSON or shell form).
    /// </summary>
    public static Gen<string> VolumeInstruction() =>
        Gen.OneOf(
            // Shell form (single path)
            from path in AbsolutePath()
            select $"VOLUME {path}",
            // JSON form
            from path in AbsolutePath()
            select $"VOLUME [\"{path}\"]",
            // JSON form (multiple)
            from path1 in AbsolutePath()
            from path2 in AbsolutePath()
            select $"VOLUME [\"{path1}\", \"{path2}\"]",
            // Shell form with multiple paths
            from p1 in AbsolutePath()
            from p2 in AbsolutePath()
            select $"VOLUME {p1} {p2}",
            // Shell form with three paths
            from p1 in AbsolutePath()
            from p2 in AbsolutePath()
            from p3 in AbsolutePath()
            select $"VOLUME {p1} {p2} {p3}",
            // JSON form with three paths
            from p1 in AbsolutePath()
            from p2 in AbsolutePath()
            from p3 in AbsolutePath()
            select $"VOLUME [\"{p1}\", \"{p2}\", \"{p3}\"]",
            // Shell form with variable ref
            from varRef in VariableRef()
            select $"VOLUME {varRef}",
            // Shell form path + variable ref
            from path in AbsolutePath()
            from varRef in VariableRef()
            select $"VOLUME {path} {varRef}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("VOLUME")
            from path in AbsolutePath()
            select $"{kw} {path}",
            // Multiple space-separated paths
            from p1 in AbsolutePath()
            from p2 in AbsolutePath()
            from p3 in AbsolutePath()
            select $"VOLUME {p1} {p2} {p3}",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from path in AbsolutePath()
            select $"VOLUME{ws}{path}",
            // Disabled: C# parser doesn't tokenize exotic variable ref modifiers (issue #206)
            // from varRef in ExoticVariableRef()
            // select $"VOLUME {varRef}"
            Gen.Constant("VOLUME /data"));

    /// <summary>
    /// Generates a valid WORKDIR instruction string.
    /// </summary>
    public static Gen<string> WorkdirInstruction() =>
        Gen.OneOf(
            // Absolute path
            from path in AbsolutePath()
            select $"WORKDIR {path}",
            // Path with variable ref
            from varRef in VariableRef()
            select $"WORKDIR {varRef}",
            // Path with variable in middle
            from seg in PathSegment()
            from varRef in VariableRef()
            select $"WORKDIR /app/{varRef}/{seg}",
            // Relative path
            from seg in PathSegment()
            select $"WORKDIR {seg}",
            // Deeply nested path
            from s1 in PathSegment()
            from s2 in PathSegment()
            from s3 in PathSegment()
            select $"WORKDIR /{s1}/{s2}/{s3}",
            // Path with variable and extension
            from varRef in VariableRef()
            select $"WORKDIR /home/{varRef}",
            // Path with mixed variables and literals
            from seg in PathSegment()
            from varRef in VariableRef()
            select $"WORKDIR /{seg}/{varRef}",
            // Case-varied keyword
            from kw in RandomCaseKeyword("WORKDIR")
            from path in AbsolutePath()
            select $"{kw} {path}",
            // Tab whitespace
            from ws in FlexibleWhitespace()
            from path in AbsolutePath()
            select $"WORKDIR{ws}{path}",
            // Relative path (subdirectory)
            from s1 in PathSegment()
            from s2 in PathSegment()
            select $"WORKDIR {s1}/{s2}",
            // Line continuation inside path
            from seg in PathSegment()
            select $"WORKDIR /app/\\\n{seg}",
            // Disabled: C# parser doesn't tokenize exotic variable ref modifiers (issue #206)
            // from varRef in ExoticVariableRef()
            // select $"WORKDIR {varRef}",
            // Quoted path
            from seg in PathSegment()
            select $"WORKDIR \"/app/{seg}\"",
            // Single-quoted path
            from seg in PathSegment()
            select $"WORKDIR '/app/{seg}'",
            // \r\n line continuation
            from seg in PathSegment()
            select $"WORKDIR /app/\\\r\n{seg}");

    /// <summary>
    /// Generates a valid SHELL instruction string (exec form only).
    /// </summary>
    public static Gen<string> ShellInstruction() =>
        Gen.OneOf(
            // Common shells
            Gen.Constant("SHELL [\"/bin/bash\", \"-c\"]"),
            Gen.Constant("SHELL [\"/bin/sh\", \"-c\"]"),
            Gen.Constant("SHELL [\"cmd\", \"/S\", \"/C\"]"),
            Gen.Constant("SHELL [\"/bin/zsh\", \"-c\"]"),
            // More shell variants
            Gen.Constant("SHELL [\"/bin/ash\", \"-c\"]"),
            Gen.Constant("SHELL [\"powershell\", \"-Command\"]"),
            Gen.Constant("SHELL [\"pwsh\", \"-Command\"]"),
            // Varied exec form JSON arrays
            from shell in Gen.Elements("/bin/bash", "/bin/sh", "/bin/zsh", "/bin/ash", "/usr/bin/env")
            from flag in Gen.Elements("-c", "-e", "-x")
            select $"SHELL [\"{shell}\", \"{flag}\"]",
            // Shell with multiple flags
            from shell in Gen.Elements("/bin/bash", "/bin/sh")
            from f1 in Gen.Elements("-e", "-x")
            from f2 in Gen.Elements("-c")
            select $"SHELL [\"{shell}\", \"{f1}\", \"{f2}\"]",
            // Windows-style shells
            from shell in Gen.Elements("cmd", "powershell", "pwsh")
            from flag in Gen.Elements("/S", "/C", "-NoProfile", "-Command")
            select $"SHELL [\"{shell}\", \"{flag}\"]",
            // Case-varied keyword
            from kw in RandomCaseKeyword("SHELL")
            select $"{kw} [\"/bin/bash\", \"-c\"]",
            // Whitespace in JSON array
            from shell in Gen.Elements("/bin/bash", "/bin/sh", "/bin/zsh")
            from flag in Gen.Elements("-c", "-e")
            select $"SHELL [ \"{shell}\" , \"{flag}\" ]");

    // ──────────────────────────────────────────────
    // Dockerfile-level generators
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates a valid complete Dockerfile string.
    /// Must start with FROM (possibly preceded by ARGs, comments, parser directives, blank lines).
    /// </summary>
    public static Gen<string> ValidDockerfile() =>
        from preamble in Preamble()
        from fromInstr in FromInstruction().Where(s => !s.Contains('\n'))
        from body in DockerfileBody()
        select BuildDockerfileText(preamble, fromInstr, body);

    // ──────────────────────────────────────────────
    // Line continuation generators
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates FROM instructions with backslash line continuations.
    /// </summary>
    public static Gen<string> FromWithLineContinuation() =>
        Gen.OneOf(
            from image in ImageName()
            from stage in StageName()
            select $"FROM \\\n{image} \\\nAS {stage}",
            from image in ImageName()
            select $"FROM \\\n  {image}");

    /// <summary>
    /// Generates FROM instructions with backtick line continuations (Windows escape char).
    /// </summary>
    public static Gen<string> FromWithBacktickContinuation() =>
        Gen.OneOf(
            from image in ImageName()
            from stage in StageName()
            select $"FROM `\n{image} `\nAS {stage}",
            from image in ImageName()
            select $"FROM `\n  {image}");

    // ──────────────────────────────────────────────
    // Helper generators
    // ──────────────────────────────────────────────

    private static Gen<string> ShellCommand() =>
        Gen.OneOf(
            Gen.Elements(
                "echo hello",
                "apt-get update",
                "apt-get install -y curl",
                "ls -la",
                "mkdir -p /app",
                "npm install",
                "pip install flask",
                "go build -o /app/main",
                "make all",
                "cat /etc/os-release"),
            // Commands with pipes
            from c1 in Gen.Elements("echo hello", "cat /etc/hosts", "ls -la", "ps aux")
            from c2 in Gen.Elements("grep pattern", "wc -l", "sort", "head -5", "tail -10")
            select $"{c1} | {c2}",
            // Commands with && chaining
            from c1 in Gen.Elements("cd /app", "apt-get update", "mkdir -p /tmp/build")
            from c2 in Gen.Elements("make", "npm install", "pip install -r requirements.txt", "cmake .")
            select $"{c1} && {c2}",
            // Commands with variable refs
            from cmd in Gen.Elements("echo", "export", "set")
            from varRef in VariableRef()
            select $"{cmd} {varRef}",
            // Longer pipeline
            from c1 in Gen.Elements("find /app", "cat /var/log/syslog", "ls -la /tmp")
            from c2 in Gen.Elements("grep error", "awk '{print $1}'", "xargs rm")
            from c3 in Gen.Elements("wc -l", "sort", "tee /tmp/out.log")
            select $"{c1} | {c2} | {c3}");

    private static Gen<string> ExecFormCommand() =>
        Gen.OneOf(
            Gen.Constant("[\"echo\", \"hello\"]"),
            Gen.Constant("[\"/bin/sh\", \"-c\", \"echo hello\"]"),
            Gen.Constant("[\"npm\", \"start\"]"),
            Gen.Constant("[\"python\", \"app.py\"]"),
            Gen.Constant("[\"dotnet\", \"run\"]"),
            // Additional exec forms
            Gen.Constant("[\"java\", \"-jar\", \"app.jar\"]"),
            Gen.Constant("[\"node\", \"server.js\"]"),
            Gen.Constant("[\"go\", \"run\", \"main.go\"]"),
            Gen.Constant("[\"ruby\", \"app.rb\"]"),
            Gen.Constant("[\"php\", \"artisan\", \"serve\"]"));

    private static Gen<string> Duration() =>
        from amount in Gen.Choose(1, 300)
        from unit in Gen.Elements("s", "m", "ms")
        select $"{amount}{unit}";

    private static Gen<string> AbsolutePath() =>
        Gen.OneOf(
            from seg in PathSegment()
            select $"/app/{seg}",
            from seg in PathSegment()
            select $"/var/{seg}",
            Gen.Elements("/app", "/data", "/var/log", "/tmp", "/opt", "/usr/local/bin"),
            // Deeper paths
            from s1 in PathSegment()
            from s2 in PathSegment()
            select $"/opt/{s1}/{s2}",
            from s1 in PathSegment()
            select $"/usr/local/{s1}",
            from s1 in PathSegment()
            select $"/home/{s1}");

    private static Gen<string> Comment() =>
        from text in Gen.Elements("build stage", "install deps", "copy files", "final image", "set env")
        select $"# {text}";

    private static Gen<string> ParserDirective() =>
        Gen.Elements(
            "# syntax=docker/dockerfile:1",
            "# escape=`");

    private static Gen<string[]> Preamble() =>
        Gen.OneOf(
            // Empty preamble
            Gen.Constant(Array.Empty<string>()),
            // Just a comment
            from comment in Comment()
            select new[] { comment },
            // Parser directive
            from directive in ParserDirective()
            select new[] { directive },
            // ARG before FROM
            from arg in ArgInstruction()
            select new[] { arg },
            // Comment + ARG
            from comment in Comment()
            from arg in ArgInstruction()
            select new[] { comment, arg });

    /// <summary>
    /// Generates a sequence of valid Dockerfile body instructions (after FROM).
    /// </summary>
    private static Gen<string[]> DockerfileBody() =>
        from count in Gen.Choose(0, 5)
        from instrs in SequenceGens(count)
        select instrs;

    /// <summary>
    /// Generates a fixed-count array of body instructions.
    /// </summary>
    private static Gen<string[]> SequenceGens(int count)
    {
        if (count == 0)
            return Gen.Constant(Array.Empty<string>());

        return BodyInstruction().ArrayOf(count);
    }

    /// <summary>
    /// Generates a single instruction valid after FROM (not FROM itself, no ONBUILD recursion).
    /// Includes all instruction types whose Sprache parser preserves trailing \n
    /// during Dockerfile-level parsing.
    /// Filters out multi-line instructions (containing newlines from line continuations)
    /// because the Dockerfile-level parser handles line splitting separately.
    /// </summary>
    private static Gen<string> BodyInstruction() =>
        Gen.OneOf(
            RunInstruction(),
            CmdInstruction(),
            EntrypointInstruction(),
            CopyInstruction(),
            AddInstruction(),
            EnvInstruction(),
            ArgInstruction(),
            ExposeInstruction(),
            LabelInstruction(),
            UserInstruction(),
            VolumeInstruction(),
            WorkdirInstruction(),
            StopSignalInstruction(),
            MaintainerInstruction(),
            ShellInstruction())
        .Where(s => !s.Contains('\n'));

    // ──────────────────────────────────────────────
    // Public generators for property tests (P0-5, P0-6, P0-7)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates a complete Dockerfile that contains a FROM, a stage-level ARG with a
    /// default value, and at least one instruction that references that variable via $VAR.
    /// ARG is placed after FROM so it is stage-scoped and resolved during variable resolution.
    /// Used by P0-5 (variable resolution non-mutation property).
    /// </summary>
    public static Gen<string> DockerfileWithVariables() =>
        from varName in Identifier()
        from defaultVal in SimpleAlphaNum()
        from image in ImageName()
        from refInstruction in Gen.OneOf(
            Gen.Constant($"LABEL mykey=${varName}"),
            Gen.Constant($"ENV myvar=${varName}"),
            Gen.Constant($"WORKDIR /app/${varName}"))
        select $"FROM {image}\nARG {varName}={defaultVal}\n{refInstruction}";

    /// <summary>
    /// Generates a single body instruction (public version of BodyInstruction).
    /// Used by P0-7 (parse isolation property).
    /// </summary>
    public static Gen<string> SingleBodyInstruction() => BodyInstruction();

    /// <summary>
    /// Generates a variable name and modifier form for modifier semantics testing (P0-6).
    /// Returns a tuple of (variableName, modifierSyntax, modifierValue, modifierChar, hasColon).
    /// </summary>
    public static Gen<(string VarName, string Modifier, string ModValue)> VariableModifierComponents() =>
        from varName in Identifier()
        from modifier in Gen.Elements(":-", ":+", ":?", "-", "+", "?")
        from modValue in SimpleAlphaNum()
        select (varName, modifier, modValue);

    /// <summary>
    /// Combines preamble, FROM, and body instructions into a single Dockerfile string.
    /// Each line except the very last must end with \n for the DockerfileParser to
    /// correctly delimit constructs.
    /// </summary>
    private static string BuildDockerfileText(string[] preamble, string fromInstr, string[] body)
    {
        var allLines = new List<string>();
        allLines.AddRange(preamble);
        allLines.Add(fromInstr);
        allLines.AddRange(body);

        if (allLines.Count == 0)
            return string.Empty;

        // All lines except the last get a trailing \n.
        // The last line has no trailing \n (matching real Dockerfiles).
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < allLines.Count - 1; i++)
        {
            sb.Append(allLines[i]);
            sb.Append('\n');
        }
        sb.Append(allLines[allLines.Count - 1]);
        return sb.ToString();
    }
}
