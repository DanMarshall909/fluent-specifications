namespace FluentSpecifications;

public enum CheckOutcome
{
    Passed,
    Failed,
    Error
}

public enum DiagnosticMode
{
    Complete,
    ShortCircuit
}

public enum RuleFailureKind
{
    Rule,
    Alternatives,
    Negation
}

public sealed record CheckOptions(DiagnosticMode Mode)
{
    public static CheckOptions Complete { get; } = new(DiagnosticMode.Complete);

    public static CheckOptions ShortCircuit { get; } = new(DiagnosticMode.ShortCircuit);
}

public sealed record RuleFailure(
    RuleFailureKind Kind,
    string RuleId,
    string Name,
    string Message,
    string? Code,
    string? Path,
    string NodePath,
    IReadOnlyDictionary<string, object?> Context,
    IReadOnlyList<RuleFailure> Causes);

public sealed record EvaluationError(
    string RuleId,
    string Name,
    string NodePath,
    Exception Exception);

public sealed class CheckResult
{
    internal CheckResult(
        CheckOutcome outcome,
        IReadOnlyList<RuleFailure> failures,
        IReadOnlyList<EvaluationError> errors,
        bool isComplete)
    {
        Outcome = outcome;
        Failures = failures;
        Errors = errors;
        IsComplete = isComplete;
    }

    public CheckOutcome Outcome { get; }

    public bool Passed => Outcome == CheckOutcome.Passed;

    public IReadOnlyList<RuleFailure> Failures { get; }

    public IReadOnlyList<EvaluationError> Errors { get; }

    public bool IsComplete { get; }
}

public sealed class SpecificationEvaluationException : Exception
{
    internal SpecificationEvaluationException(
        string ruleId,
        string ruleName,
        string nodePath,
        Exception innerException)
        : base($"Rule '{ruleId}' could not be evaluated at '{nodePath}'.", innerException)
    {
        RuleId = ruleId;
        RuleName = ruleName;
        NodePath = nodePath;
    }

    public string RuleId { get; }

    public string RuleName { get; }

    public string NodePath { get; }
}
