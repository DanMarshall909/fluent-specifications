using System.Linq.Expressions;

namespace FluentSpecifications;

public sealed record RuleDescriptor
{
    public RuleDescriptor(
        string id,
        string name,
        string? failure,
        string? code,
        string? path,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        Id = id;
        Name = name;
        Failure = failure;
        Code = code;
        Path = path;
        Context = context is null
            ? EmptyContext.Value
            : new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(context, StringComparer.Ordinal));
    }

    public string Id { get; }

    public string Name { get; }

    public string? Failure { get; }

    public string? Code { get; }

    public string? Path { get; }

    public IReadOnlyDictionary<string, object?> Context { get; }

    private static class EmptyContext
    {
        public static readonly IReadOnlyDictionary<string, object?> Value =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>());
    }
}

public interface ISpecVisitor<T, out TResult>
{
    TResult VisitAlways();

    TResult VisitNever();

    TResult VisitLeaf(
        RuleDescriptor rule,
        Expression<Func<T, bool>> predicate);

    TResult VisitNamed(RuleDescriptor rule, Spec<T> child);

    TResult VisitAnd(Spec<T> left, Spec<T> right);

    TResult VisitOr(Spec<T> left, Spec<T> right);

    TResult VisitNot(Spec<T> child);
}
