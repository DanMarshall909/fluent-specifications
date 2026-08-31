using System.Diagnostics;
using System.Linq.Expressions;

namespace FluentSpecifications;

public delegate Spec<T> SpecConnector<T>(Spec<T> right);

public sealed class Spec<T>
{
    private readonly SpecNode<T> _node;

    internal Spec(SpecNode<T> node)
    {
        _node = node;
    }

    public SpecConnector<T> And => right => Compose(right, static (left, value) => new AndNode<T>(left, value));

    public SpecConnector<T> Or => right => Compose(right, static (left, value) => new OrNode<T>(left, value));

    public SpecConnector<T> AndNot => right =>
        Compose(right, static (left, value) => new AndNode<T>(left, new NotNode<T>(value)));

    public SpecConnector<T> OrNot => right =>
        Compose(right, static (left, value) => new OrNode<T>(left, new NotNode<T>(value)));

    public Spec<T> Not => _node switch
    {
        NotNode<T> not => new Spec<T>(not.Child),
        AlwaysNode<T> => Spec.Never<T>(),
        NeverNode<T> => Spec.Always<T>(),
        _ => new Spec<T>(new NotNode<T>(_node))
    };

    public bool Matches(T candidate) => SpecEvaluator.Matches(_node, candidate);

    public CheckResult Check(T candidate, CheckOptions? options = null) =>
        SpecEvaluator.Check(_node, candidate, options ?? CheckOptions.Complete);

    public TResult Accept<TResult>(ISpecVisitor<T, TResult> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return _node switch
        {
            AlwaysNode<T> => visitor.VisitAlways(),
            NeverNode<T> => visitor.VisitNever(),
            LeafNode<T> leaf => visitor.VisitLeaf(leaf.Metadata, leaf.Predicate),
            NamedNode<T> named => visitor.VisitNamed(
                named.Metadata,
                new Spec<T>(named.Child)),
            AndNode<T> and => visitor.VisitAnd(
                new Spec<T>(and.Left),
                new Spec<T>(and.Right)),
            OrNode<T> or => visitor.VisitOr(
                new Spec<T>(or.Left),
                new Spec<T>(or.Right)),
            NotNode<T> not => visitor.VisitNot(new Spec<T>(not.Child)),
            _ => throw new UnreachableException($"Unknown specification node: {_node.GetType().Name}.")
        };
    }

    public Spec<T> Named(
        string id,
        string name,
        string? failure = null,
        string? code = null,
        string? path = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Spec<T>(new NamedNode<T>(
            new RuleDescriptor(id, name, failure, code, path, context),
            _node));
    }

    public override string ToString() => SpecRenderer.Render(_node);

    private Spec<T> Compose(
        Spec<T> right,
        Func<SpecNode<T>, SpecNode<T>, SpecNode<T>> compose)
    {
        ArgumentNullException.ThrowIfNull(right);
        return new Spec<T>(compose(_node, right._node));
    }
}

public static class Spec
{
    public static Spec<T> Define<T>(
        string id,
        string name,
        Expression<Func<T, bool>> predicate,
        string? failure = null,
        string? code = null,
        string? path = null,
        IReadOnlyDictionary<string, object?>? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(predicate);

        return new Spec<T>(new LeafNode<T>(
            new RuleDescriptor(id, name, failure, code, path, context),
            predicate));
    }

    public static Spec<T> Always<T>() => SpecConstants<T>.Always;

    public static Spec<T> Never<T>() => SpecConstants<T>.Never;

    public static Spec<T> AllOf<T>(IEnumerable<Spec<T>> specifications)
    {
        var snapshot = Snapshot(specifications);
        if (snapshot.Length == 0)
        {
            return Always<T>();
        }

        var result = snapshot[0];
        for (var index = 1; index < snapshot.Length; index++)
        {
            result = result.And(snapshot[index]);
        }

        return result;
    }

    public static Spec<T> AnyOf<T>(IEnumerable<Spec<T>> specifications)
    {
        var snapshot = Snapshot(specifications);
        if (snapshot.Length == 0)
        {
            return Never<T>();
        }

        var result = snapshot[0];
        for (var index = 1; index < snapshot.Length; index++)
        {
            result = result.Or(snapshot[index]);
        }

        return result;
    }

    private static Spec<T>[] Snapshot<T>(IEnumerable<Spec<T>> specifications)
    {
        ArgumentNullException.ThrowIfNull(specifications);
        var snapshot = specifications.ToArray();

        if (snapshot.Any(static specification => specification is null))
        {
            throw new ArgumentException(
                "A specification collection cannot contain null elements.",
                nameof(specifications));
        }

        return snapshot;
    }
}

internal static class SpecConstants<T>
{
    public static readonly Spec<T> Always = new(new AlwaysNode<T>());

    public static readonly Spec<T> Never = new(new NeverNode<T>());
}

internal abstract record SpecNode<T>;

internal sealed record AlwaysNode<T> : SpecNode<T>;

internal sealed record NeverNode<T> : SpecNode<T>;

internal sealed record NamedNode<T>(RuleDescriptor Metadata, SpecNode<T> Child) : SpecNode<T>;

internal sealed record LeafNode<T> : SpecNode<T>
{
    private readonly Lazy<Func<T, bool>> _compiled;

    public LeafNode(RuleDescriptor metadata, Expression<Func<T, bool>> predicate)
    {
        Metadata = metadata;
        Predicate = predicate;
        _compiled = new Lazy<Func<T, bool>>(
            predicate.Compile,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public RuleDescriptor Metadata { get; }

    public Expression<Func<T, bool>> Predicate { get; }

    public bool Matches(T candidate) => _compiled.Value(candidate);
}

internal sealed record AndNode<T>(SpecNode<T> Left, SpecNode<T> Right) : SpecNode<T>;

internal sealed record OrNode<T>(SpecNode<T> Left, SpecNode<T> Right) : SpecNode<T>;

internal sealed record NotNode<T>(SpecNode<T> Child) : SpecNode<T>;

internal static class SpecEvaluator
{
    public static bool Matches<T>(SpecNode<T> node, T candidate) =>
        Matches(node, candidate, "$");

    public static CheckResult Check<T>(
        SpecNode<T> node,
        T candidate,
        CheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var result = CheckNode(node, candidate, options.Mode, "$");
        return new CheckResult(result.Outcome, result.Failures, result.Errors, result.IsComplete);
    }

    private static bool Matches<T>(SpecNode<T> node, T candidate, string nodePath) => node switch
    {
        AlwaysNode<T> => true,
        NeverNode<T> => false,
        LeafNode<T> leaf => MatchLeaf(leaf, candidate, nodePath),
        NamedNode<T> named => Matches(named.Child, candidate, $"{nodePath}.rule"),
        AndNode<T> and =>
            Matches(and.Left, candidate, $"{nodePath}.left") &&
            Matches(and.Right, candidate, $"{nodePath}.right"),
        OrNode<T> or =>
            Matches(or.Left, candidate, $"{nodePath}.left") ||
            Matches(or.Right, candidate, $"{nodePath}.right"),
        NotNode<T> not => !Matches(not.Child, candidate, $"{nodePath}.not"),
        _ => throw new UnreachableException($"Unknown specification node: {node.GetType().Name}.")
    };

    private static bool MatchLeaf<T>(LeafNode<T> leaf, T candidate, string nodePath)
    {
        try
        {
            return leaf.Matches(candidate);
        }
        catch (Exception exception)
        {
            throw new SpecificationEvaluationException(
                leaf.Metadata.Id,
                leaf.Metadata.Name,
                nodePath,
                exception);
        }
    }

    private static NodeCheck CheckNode<T>(
        SpecNode<T> node,
        T candidate,
        DiagnosticMode mode,
        string nodePath) => node switch
        {
            AlwaysNode<T> => NodeCheck.Passed,
            NeverNode<T> => NodeCheck.Failed(Failure(
                RuleFailureKind.Rule,
                new RuleDescriptor("spec.never", "Never", "The rule never matches.", null, null),
                nodePath)),
            LeafNode<T> leaf => CheckLeaf(leaf, candidate, nodePath),
            NamedNode<T> named => CheckNamed(named, candidate, mode, nodePath),
            AndNode<T> and => CheckAnd(and, candidate, mode, nodePath),
            OrNode<T> or => CheckOr(or, candidate, mode, nodePath),
            NotNode<T> not => CheckNot(not, candidate, mode, nodePath),
            _ => throw new UnreachableException($"Unknown specification node: {node.GetType().Name}.")
        };

    private static NodeCheck CheckLeaf<T>(LeafNode<T> leaf, T candidate, string nodePath)
    {
        try
        {
            return leaf.Matches(candidate)
                ? NodeCheck.Passed
                : NodeCheck.Failed(Failure(RuleFailureKind.Rule, leaf.Metadata, nodePath));
        }
        catch (Exception exception)
        {
            return NodeCheck.Error(new EvaluationError(
                leaf.Metadata.Id,
                leaf.Metadata.Name,
                nodePath,
                exception));
        }
    }

    private static NodeCheck CheckNamed<T>(
        NamedNode<T> named,
        T candidate,
        DiagnosticMode mode,
        string nodePath)
    {
        var child = CheckNode(named.Child, candidate, mode, $"{nodePath}.rule");
        if (child.Outcome != CheckOutcome.Failed)
        {
            return child;
        }

        return new NodeCheck(
            CheckOutcome.Failed,
            [Failure(RuleFailureKind.Rule, named.Metadata, nodePath, child.Failures)],
            child.Errors,
            child.IsComplete);
    }

    private static NodeCheck CheckAnd<T>(
        AndNode<T> and,
        T candidate,
        DiagnosticMode mode,
        string nodePath)
    {
        var left = CheckNode(and.Left, candidate, mode, $"{nodePath}.left");
        if (mode == DiagnosticMode.ShortCircuit && left.Outcome == CheckOutcome.Failed)
        {
            return left with { IsComplete = false };
        }

        var right = CheckNode(and.Right, candidate, mode, $"{nodePath}.right");
        var outcome = left.Outcome == CheckOutcome.Failed || right.Outcome == CheckOutcome.Failed
            ? CheckOutcome.Failed
            : left.Outcome == CheckOutcome.Error || right.Outcome == CheckOutcome.Error
                ? CheckOutcome.Error
                : CheckOutcome.Passed;

        var failures = outcome == CheckOutcome.Failed
            ? FailuresForFailedChildren(left, right)
            : [];

        return new NodeCheck(
            outcome,
            failures,
            [.. left.Errors, .. right.Errors],
            left.IsComplete && right.IsComplete);
    }

    private static NodeCheck CheckOr<T>(
        OrNode<T> or,
        T candidate,
        DiagnosticMode mode,
        string nodePath)
    {
        var left = CheckNode(or.Left, candidate, mode, $"{nodePath}.left");
        if (mode == DiagnosticMode.ShortCircuit && left.Outcome == CheckOutcome.Passed)
        {
            return left with { IsComplete = false };
        }

        var right = CheckNode(or.Right, candidate, mode, $"{nodePath}.right");
        var outcome = left.Outcome == CheckOutcome.Passed || right.Outcome == CheckOutcome.Passed
            ? CheckOutcome.Passed
            : left.Outcome == CheckOutcome.Error || right.Outcome == CheckOutcome.Error
                ? CheckOutcome.Error
                : CheckOutcome.Failed;

        IReadOnlyList<RuleFailure> failures = outcome == CheckOutcome.Failed
            ? [new RuleFailure(
                RuleFailureKind.Alternatives,
                "spec.or",
                "Any alternative",
                "None of the alternatives matched.",
                null,
                null,
                nodePath,
                EmptyDiagnosticContext.Value,
                [.. left.Failures, .. right.Failures])]
            : [];

        return new NodeCheck(
            outcome,
            failures,
            [.. left.Errors, .. right.Errors],
            left.IsComplete && right.IsComplete);
    }

    private static NodeCheck CheckNot<T>(
        NotNode<T> not,
        T candidate,
        DiagnosticMode mode,
        string nodePath)
    {
        var child = CheckNode(not.Child, candidate, mode, $"{nodePath}.not");
        return child.Outcome switch
        {
            CheckOutcome.Passed => new NodeCheck(
                CheckOutcome.Failed,
                [new RuleFailure(
                    RuleFailureKind.Negation,
                    "spec.not",
                    "Not",
                    "Expected the rule not to match.",
                    null,
                    null,
                    nodePath,
                    EmptyDiagnosticContext.Value,
                    [])],
                child.Errors,
                child.IsComplete),
            CheckOutcome.Failed => new NodeCheck(
                CheckOutcome.Passed,
                [],
                child.Errors,
                child.IsComplete),
            _ => child
        };
    }

    private static RuleFailure Failure(
        RuleFailureKind kind,
        RuleDescriptor metadata,
        string nodePath,
        IReadOnlyList<RuleFailure>? causes = null) => new(
            kind,
            metadata.Id,
            metadata.Name,
            metadata.Failure ?? $"{metadata.Name} did not match.",
            metadata.Code,
            metadata.Path,
            nodePath,
            metadata.Context,
            causes ?? []);

    private static IReadOnlyList<RuleFailure> FailuresForFailedChildren(
        NodeCheck left,
        NodeCheck right)
    {
        var failures = new List<RuleFailure>();
        if (left.Outcome == CheckOutcome.Failed)
        {
            failures.AddRange(left.Failures);
        }

        if (right.Outcome == CheckOutcome.Failed)
        {
            failures.AddRange(right.Failures);
        }

        return failures;
    }

    private sealed record NodeCheck(
        CheckOutcome Outcome,
        IReadOnlyList<RuleFailure> Failures,
        IReadOnlyList<EvaluationError> Errors,
        bool IsComplete)
    {
        public static NodeCheck Passed { get; } = new(CheckOutcome.Passed, [], [], true);

        public static NodeCheck Failed(RuleFailure failure) =>
            new(CheckOutcome.Failed, [failure], [], true);

        public static NodeCheck Error(EvaluationError error) =>
            new(CheckOutcome.Error, [], [error], true);
    }

    private static class EmptyDiagnosticContext
    {
        public static readonly IReadOnlyDictionary<string, object?> Value =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>());
    }
}

internal static class SpecRenderer
{
    public static string Render<T>(SpecNode<T> node) => Render(node, parentPrecedence: 0);

    private static string Render<T>(SpecNode<T> node, int parentPrecedence)
    {
        var precedence = Precedence(node);
        var text = node switch
        {
            AlwaysNode<T> => "Always",
            NeverNode<T> => "Never",
            LeafNode<T> leaf => leaf.Metadata.Name,
            NamedNode<T> named => named.Metadata.Name,
            AndNode<T> and =>
                $"{Render(and.Left, precedence)} AND {Render(and.Right, precedence)}",
            OrNode<T> or =>
                $"{Render(or.Left, precedence)} OR {Render(or.Right, precedence)}",
            NotNode<T> not => $"NOT {Render(not.Child, precedence)}",
            _ => throw new UnreachableException($"Unknown specification node: {node.GetType().Name}.")
        };

        return precedence < parentPrecedence ? $"({text})" : text;
    }

    private static int Precedence<T>(SpecNode<T> node) => node switch
    {
        OrNode<T> => 1,
        AndNode<T> => 2,
        NotNode<T> => 3,
        _ => 4
    };
}
