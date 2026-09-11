using System.Text;

namespace Valleysoft.DockerfileModel.DiffTest;

internal static class InstructionTypes
{
    private static readonly HashSet<string> Supported = new(
        new[]
        {
            "ADD", "ARG", "CMD", "COPY", "ENTRYPOINT", "ENV", "EXPOSE",
            "FROM", "HEALTHCHECK", "LABEL", "MAINTAINER", "ONBUILD", "RUN",
            "SHELL", "STOPSIGNAL", "USER", "VOLUME", "WORKDIR"
        },
        StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string instructionType) =>
        Supported.Contains(instructionType);
}

public enum DiffCaseSource
{
    Regression,
    Generated,
    Replay
}

public enum DiffOutcomeKind
{
    Match,
    JsonMismatch,
    CSharpParseError,
    CSharpParserCrash,
    LeanParseError,
    InfrastructureError
}

public sealed record DiffCase(
    string Id,
    DiffCaseSource Source,
    string InstructionType,
    string Input,
    char EscapeChar,
    string? Generator = null,
    int? Seed = null,
    int? CaseIndex = null)
{
    public string InputBase64 => Convert.ToBase64String(Encoding.UTF8.GetBytes(Input));
}

public sealed record DiffResult(
    DiffCase Case,
    string CSharpJson,
    string LeanJson,
    DiffOutcomeKind Outcome,
    string? Error = null)
{
    public bool Match => Outcome == DiffOutcomeKind.Match;
}

public sealed record RegressionFixture(
    string Id,
    string InstructionType,
    string InputBase64,
    string EscapeCharacter,
    string? Generator = null,
    int? Seed = null,
    int? CaseIndex = null)
{
    public DiffCase ToCase()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            throw new InvalidDataException("Regression fixture must have a non-empty id.");
        }

        if (string.IsNullOrWhiteSpace(InstructionType))
        {
            throw new InvalidDataException(
                $"Regression fixture '{Id}' must have a non-empty instructionType.");
        }

        if (!InstructionTypes.IsSupported(InstructionType))
        {
            throw new InvalidDataException(
                $"Regression fixture '{Id}' has unsupported instructionType '{InstructionType}'.");
        }

        if (InputBase64 is null)
        {
            throw new InvalidDataException(
                $"Regression fixture '{Id}' must have an inputBase64 value.");
        }

        if (EscapeCharacter is null || EscapeCharacter.Length != 1)
        {
            throw new InvalidDataException(
                $"Regression fixture '{Id}' must have a one-character escapeCharacter.");
        }

        string input;
        try
        {
            input = Encoding.UTF8.GetString(Convert.FromBase64String(InputBase64));
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException(
                $"Regression fixture '{Id}' has invalid base64 input.", ex);
        }

        return new DiffCase(
            Id,
            DiffCaseSource.Regression,
            InstructionType,
            input,
            EscapeCharacter[0],
            Generator,
            Seed,
            CaseIndex);
    }

    public static RegressionFixture FromCase(DiffCase testCase) =>
        new(
            testCase.Id,
            testCase.InstructionType,
            testCase.InputBase64,
            testCase.EscapeChar.ToString(),
            testCase.Generator,
            testCase.Seed,
            testCase.CaseIndex);
}
