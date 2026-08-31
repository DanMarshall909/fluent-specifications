using System.Linq.Expressions;

namespace FluentSpecifications.Expressions;

public sealed class ExpressionSpecTranslator<T> :
    ISpecTranslator<T, Expression<Func<T, bool>>>,
    ISpecVisitor<T, Expression<Func<T, bool>>>
{
    private readonly ParameterExpression _candidate =
        Expression.Parameter(typeof(T), "candidate");

    public Preparation<Expression<Func<T, bool>>> Prepare(Spec<T> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return Preparation<Expression<Func<T, bool>>>.Succeeded(specification.Accept(this));
    }

    public Expression<Func<T, bool>> VisitAlways() =>
        Lambda(Expression.Constant(true));

    public Expression<Func<T, bool>> VisitNever() =>
        Lambda(Expression.Constant(false));

    public Expression<Func<T, bool>> VisitLeaf(
        RuleDescriptor rule,
        Expression<Func<T, bool>> predicate)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(predicate);

        var body = new ParameterReplacer(predicate.Parameters[0], _candidate)
            .Visit(predicate.Body);
        return Lambda(body);
    }

    public Expression<Func<T, bool>> VisitNamed(RuleDescriptor rule, Spec<T> child)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(child);
        return child.Accept(this);
    }

    public Expression<Func<T, bool>> VisitAnd(Spec<T> left, Spec<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Lambda(Expression.AndAlso(left.Accept(this).Body, right.Accept(this).Body));
    }

    public Expression<Func<T, bool>> VisitOr(Spec<T> left, Spec<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return Lambda(Expression.OrElse(left.Accept(this).Body, right.Accept(this).Body));
    }

    public Expression<Func<T, bool>> VisitNot(Spec<T> child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return Lambda(Expression.Not(child.Accept(this).Body));
    }

    private Expression<Func<T, bool>> Lambda(Expression body) =>
        Expression.Lambda<Func<T, bool>>(body, _candidate);

    private sealed class ParameterReplacer(
        ParameterExpression source,
        ParameterExpression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? replacement : base.VisitParameter(node);
    }
}
