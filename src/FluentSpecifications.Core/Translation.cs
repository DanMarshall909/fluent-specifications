namespace FluentSpecifications;

public interface ISpecTranslator<T, TPlan>
{
    Preparation<TPlan> Prepare(Spec<T> specification);
}

public sealed record TranslationError
{
    public TranslationError(
        string code,
        string message,
        string nodePath,
        string? ruleId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(nodePath);

        Code = code;
        Message = message;
        NodePath = nodePath;
        RuleId = ruleId;
    }

    public string Code { get; }

    public string Message { get; }

    public string NodePath { get; }

    public string? RuleId { get; }
}

public sealed class Preparation<TPlan>
{
    private readonly TPlan? _plan;

    private Preparation(
        bool isSuccess,
        TPlan? plan,
        IReadOnlyList<TranslationError> errors)
    {
        IsSuccess = isSuccess;
        _plan = plan;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public IReadOnlyList<TranslationError> Errors { get; }

    public static Preparation<TPlan> Succeeded(TPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new Preparation<TPlan>(true, plan, Array.Empty<TranslationError>());
    }

    public static Preparation<TPlan> Failed(IEnumerable<TranslationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var snapshot = errors.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A failed preparation requires at least one translation error.",
                nameof(errors));
        }

        if (snapshot.Any(static error => error is null))
        {
            throw new ArgumentException(
                "A translation error collection cannot contain null elements.",
                nameof(errors));
        }

        return new Preparation<TPlan>(
            false,
            default,
            Array.AsReadOnly(snapshot));
    }

    public TPlan GetPlanOrThrow()
    {
        if (!IsSuccess)
        {
            throw new SpecificationTranslationException(Errors);
        }

        return _plan!;
    }
}

public sealed class SpecificationTranslationException : Exception
{
    internal SpecificationTranslationException(IReadOnlyList<TranslationError> errors)
        : base($"Specification translation failed with {errors.Count} error(s).")
    {
        Errors = errors;
    }

    public IReadOnlyList<TranslationError> Errors { get; }
}
