using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public enum AnalysisDiagnosticSeverity
{
    Warning,
    Error
}

public enum AnalysisDiagnosticCode
{
    DuplicateStageName,
    InvalidStageIndex,
    ForwardStageReference,
    CircularStageDependency,
    UnresolvedVariable,
    UnsupportedVariableExpansion,
    VariableSubstitutionFailed,
    UnsupportedEvaluation,
    InvalidReference,
    InvalidMountSource,
    InstructionBeforeFrom
}

public sealed class AnalysisDiagnostic
{
    internal AnalysisDiagnostic(AnalysisDiagnosticCode code, AnalysisDiagnosticSeverity severity,
        string message, Instruction instruction, Token? operandToken = null, AnalyzedStage? stage = null,
        IEnumerable<int>? relatedStageIndices = null, IEnumerable<string>? variableNames = null)
    {
        Code = code;
        Severity = severity;
        Message = message;
        Instruction = instruction;
        OperandToken = operandToken;
        Stage = stage;
        RelatedStageIndices = Array.AsReadOnly((relatedStageIndices ?? Enumerable.Empty<int>()).ToArray());
        VariableNames = Array.AsReadOnly((variableNames ?? Enumerable.Empty<string>()).Distinct().ToArray());
    }

    public AnalysisDiagnosticCode Code { get; }
    public AnalysisDiagnosticSeverity Severity { get; }
    public string Message { get; }
    public Instruction Instruction { get; }
    public Token? OperandToken { get; }
    public AnalyzedStage? Stage { get; }
    public IReadOnlyList<int> RelatedStageIndices { get; }
    public IReadOnlyList<string> VariableNames { get; }
}
