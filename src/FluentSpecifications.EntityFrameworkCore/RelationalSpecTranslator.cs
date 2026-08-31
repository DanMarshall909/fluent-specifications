using System.Linq.Expressions;
using FluentSpecifications.Expressions;
using Microsoft.EntityFrameworkCore;

namespace FluentSpecifications.EntityFrameworkCore;

public sealed class RelationalSpecTranslator<T> :
    ISpecTranslator<T, Expression<Func<T, bool>>>
    where T : class
{
    private readonly DbContext _context;
    private readonly ExpressionSpecTranslator<T> _expressionTranslator = new();

    public RelationalSpecTranslator(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    public Preparation<Expression<Func<T, bool>>> Prepare(Spec<T> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        if (!_context.Database.IsRelational())
        {
            return Preparation<Expression<Func<T, bool>>>.Failed(
            [
                new TranslationError(
                    "ef-core-provider-not-relational",
                    "Relational specification preparation requires an EF Core relational provider.",
                    "$")
            ]);
        }

        var expression = _expressionTranslator.Prepare(specification).GetPlanOrThrow();
        try
        {
            _ = _context.Set<T>().Where(expression).ToQueryString();
            return Preparation<Expression<Func<T, bool>>>.Succeeded(expression);
        }
        catch (Exception exception) when (IsTranslationException(exception))
        {
            var leafErrors = Preflight(specification, "$");
            if (leafErrors.Count > 0)
            {
                return Preparation<Expression<Func<T, bool>>>.Failed(leafErrors);
            }

            return Preparation<Expression<Func<T, bool>>>.Failed(
            [
                new TranslationError(
                    "ef-core-composition-translation-failed",
                    "EF Core could not translate the composed specification.",
                    "$")
            ]);
        }
    }

    private IReadOnlyList<TranslationError> Preflight(Spec<T> specification, string nodePath) =>
        specification.Accept(new PreflightVisitor(_context, nodePath, Preflight));

    private static bool IsTranslationException(Exception exception) =>
        exception is InvalidOperationException or NotSupportedException;

    private sealed class PreflightVisitor(
        DbContext context,
        string nodePath,
        Func<Spec<T>, string, IReadOnlyList<TranslationError>> visit) :
        ISpecVisitor<T, IReadOnlyList<TranslationError>>
    {
        public IReadOnlyList<TranslationError> VisitAlways() => [];

        public IReadOnlyList<TranslationError> VisitNever() => [];

        public IReadOnlyList<TranslationError> VisitLeaf(
            RuleDescriptor rule,
            Expression<Func<T, bool>> predicate)
        {
            try
            {
                _ = context.Set<T>().Where(predicate).ToQueryString();
                return [];
            }
            catch (Exception exception) when (IsTranslationException(exception))
            {
                return
                [
                    new TranslationError(
                        "ef-core-translation-failed",
                        $"EF Core could not translate rule '{rule.Name}'.",
                        nodePath,
                        rule.Id)
                ];
            }
        }

        public IReadOnlyList<TranslationError> VisitNamed(RuleDescriptor rule, Spec<T> child) =>
            visit(child, $"{nodePath}.rule");

        public IReadOnlyList<TranslationError> VisitAnd(Spec<T> left, Spec<T> right) =>
            Combine(
                visit(left, $"{nodePath}.left"),
                visit(right, $"{nodePath}.right"));

        public IReadOnlyList<TranslationError> VisitOr(Spec<T> left, Spec<T> right) =>
            Combine(
                visit(left, $"{nodePath}.left"),
                visit(right, $"{nodePath}.right"));

        public IReadOnlyList<TranslationError> VisitNot(Spec<T> child) =>
            visit(child, $"{nodePath}.not");

        private static IReadOnlyList<TranslationError> Combine(
            IReadOnlyList<TranslationError> left,
            IReadOnlyList<TranslationError> right) =>
            left.Count == 0
                ? right
                : right.Count == 0
                    ? left
                    : [.. left, .. right];
    }
}
