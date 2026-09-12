namespace Valleysoft.DockerfileModel;

public sealed class DockerfileDiagnostic
{
    public DockerfileDiagnostic(string code, DiagnosticSeverity severity, string message, SourceSpan sourceSpan)
    {
        Guard.NotNullOrEmpty(code, nameof(code));
        Guard.NotNullOrEmpty(message, nameof(message));
        if (severity is not DiagnosticSeverity.Info and not DiagnosticSeverity.Warning and not DiagnosticSeverity.Error)
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        Code = code;
        Severity = severity;
        Message = message;
        SourceSpan = sourceSpan;
    }

    public string Code { get; }
    public DiagnosticSeverity Severity { get; }
    public string Message { get; }
    public SourceSpan SourceSpan { get; }
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public static class DockerfileDiagnosticCodes
{
    public const string InvalidSyntax = "DFP001";
    public const string UnknownInstruction = "DFP002";
    public const string UnterminatedHeredoc = "DFP003";
    public const string InvalidParserDirective = "DFP004";
}
