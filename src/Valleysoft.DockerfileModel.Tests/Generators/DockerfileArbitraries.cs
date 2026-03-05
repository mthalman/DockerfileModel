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
            Gen.Elements("SIGTERM", "SIGKILL", "SIGINT", "SIGQUIT", "SIGHUP", "SIGUSR1"),
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
            select $"FROM --platform={platform} {image} AS {stage}");

    /// <summary>
    /// Generates a valid RUN instruction string (shell form or exec form).
    /// </summary>
    public static Gen<string> RunInstruction() =>
        Gen.OneOf(
            // Shell form
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
            select $"RUN --security={security} {cmd}");

    /// <summary>
    /// Generates a valid CMD instruction string.
    /// </summary>
    public static Gen<string> CmdInstruction() =>
        Gen.OneOf(
            // Shell form
            from cmd in ShellCommand()
            select $"CMD {cmd}",
            // Exec form
            from cmd in ExecFormCommand()
            select $"CMD {cmd}");

    /// <summary>
    /// Generates a valid ENTRYPOINT instruction string.
    /// </summary>
    public static Gen<string> EntrypointInstruction() =>
        Gen.OneOf(
            // Shell form
            from cmd in ShellCommand()
            select $"ENTRYPOINT {cmd}",
            // Exec form
            from cmd in ExecFormCommand()
            select $"ENTRYPOINT {cmd}");

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
            select $"COPY --chmod={mode} {src} {dst}");

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
            select $"ADD --link {src} {dst}");

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
            select $"ENV {key} {value}");

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
            select $"ARG {name}={value}");

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
            select $"EXPOSE {port}/{proto}");

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
            select $"HEALTHCHECK --interval={interval} --timeout={timeout} --retries={retries} CMD {cmd}");

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
            select $"LABEL {key}=\"{value}\"");

    /// <summary>
    /// Generates a valid MAINTAINER instruction string.
    /// </summary>
    public static Gen<string> MaintainerInstruction() =>
        from name in Gen.Elements("John", "Jane", "Alice", "Bob")
        from domain in Gen.Elements("example.com", "test.org", "dev.io")
        select $"MAINTAINER {name} <{name}@{domain}>";

    /// <summary>
    /// Generates a valid ONBUILD instruction string.
    /// Wraps a subset of instructions that are valid inside ONBUILD.
    /// ONBUILD cannot wrap FROM or ONBUILD.
    /// </summary>
    public static Gen<string> OnBuildInstruction() =>
        Gen.OneOf(
            from inner in RunInstruction()
            select $"ONBUILD {inner}",
            from inner in CopyInstruction()
            select $"ONBUILD {inner}",
            from inner in AddInstruction()
            select $"ONBUILD {inner}",
            from key in Identifier()
            from value in SimpleAlphaNum()
            select $"ONBUILD ENV {key}={value}");

    /// <summary>
    /// Generates a valid STOPSIGNAL instruction string.
    /// </summary>
    public static Gen<string> StopSignalInstruction() =>
        from signal in Signal()
        select $"STOPSIGNAL {signal}";

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
            select $"USER {uid}:{gid}");

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
            select $"VOLUME [\"{path1}\", \"{path2}\"]");

    /// <summary>
    /// Generates a valid WORKDIR instruction string.
    /// </summary>
    public static Gen<string> WorkdirInstruction() =>
        from path in AbsolutePath()
        select $"WORKDIR {path}";

    /// <summary>
    /// Generates a valid SHELL instruction string (exec form only).
    /// </summary>
    public static Gen<string> ShellInstruction() =>
        Gen.OneOf(
            Gen.Constant("SHELL [\"/bin/bash\", \"-c\"]"),
            Gen.Constant("SHELL [\"/bin/sh\", \"-c\"]"),
            Gen.Constant("SHELL [\"cmd\", \"/S\", \"/C\"]"),
            Gen.Constant("SHELL [\"/bin/zsh\", \"-c\"]"));

    // ──────────────────────────────────────────────
    // Dockerfile-level generators
    // ──────────────────────────────────────────────

    /// <summary>
    /// Generates a valid complete Dockerfile string.
    /// Must start with FROM (possibly preceded by ARGs, comments, parser directives, blank lines).
    /// </summary>
    public static Gen<string> ValidDockerfile() =>
        from preamble in Preamble()
        from fromInstr in FromInstruction()
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
            "cat /etc/os-release");

    private static Gen<string> ExecFormCommand() =>
        Gen.OneOf(
            Gen.Constant("[\"echo\", \"hello\"]"),
            Gen.Constant("[\"/bin/sh\", \"-c\", \"echo hello\"]"),
            Gen.Constant("[\"npm\", \"start\"]"),
            Gen.Constant("[\"python\", \"app.py\"]"),
            Gen.Constant("[\"dotnet\", \"run\"]"));

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
            Gen.Elements("/app", "/data", "/var/log", "/tmp", "/opt", "/usr/local/bin"));

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
            ShellInstruction());

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
