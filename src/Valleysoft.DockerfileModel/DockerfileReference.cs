using Valleysoft.DockerfileModel.Tokens;

namespace Valleysoft.DockerfileModel;

public enum DockerfileReferenceKind
{
    BaseStage,
    CopySource,
    MountSource
}

public enum DockerfileReferenceClassification
{
    Stage,
    ExternalImage,
    Scratch,
    Unresolved,
    Invalid,
    Deferred,
    BuildContext
}

public enum DockerfileReferencePhase
{
    Immediate,
    DeferredOnBuild,
    InheritedOnBuild
}

/// <summary>
/// One source-selector occurrence. Inherited ONBUILD occurrences retain the declaration's
/// instruction and token, but SourceStage identifies the child executing the trigger.
/// </summary>
public sealed class DockerfileReference
{
    private readonly List<AnalysisDiagnostic> diagnostics = new();

    internal DockerfileReference(DockerfileReferenceKind kind,
        DockerfileReferenceClassification classification, DockerfileReferencePhase phase,
        string? resolvedValue, Instruction instruction, Token operandToken,
        AnalyzedStage declaringStage, AnalyzedStage? sourceStage, AnalyzedStage? targetStage,
        OnBuildInstruction? onBuildInstruction)
    {
        Kind = kind;
        Classification = classification;
        Phase = phase;
        OriginalText = operandToken.ToString();
        ResolvedValue = resolvedValue;
        Instruction = instruction;
        OperandToken = operandToken;
        DeclaringStage = declaringStage;
        SourceStage = sourceStage;
        TargetStage = targetStage;
        OnBuildInstruction = onBuildInstruction;
        Diagnostics = diagnostics.AsReadOnly();
    }

    public DockerfileReferenceKind Kind { get; }
    public DockerfileReferenceClassification Classification { get; }
    public DockerfileReferencePhase Phase { get; }
    public string OriginalText { get; }
    public string? ResolvedValue { get; }
    public Instruction Instruction { get; }

    /// <summary>
    /// The original selector token, or the whole Mount or ONBUILD trigger instruction
    /// when effective fields cannot be mapped faithfully. Unresolved references are not safe automatic edit targets.
    /// </summary>
    public Token OperandToken { get; }
    public AnalyzedStage DeclaringStage { get; }
    public AnalyzedStage? SourceStage { get; }
    public AnalyzedStage? TargetStage { get; }
    public OnBuildInstruction? OnBuildInstruction { get; }
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }

    internal void AddDiagnostic(AnalysisDiagnostic diagnostic) => diagnostics.Add(diagnostic);
}
