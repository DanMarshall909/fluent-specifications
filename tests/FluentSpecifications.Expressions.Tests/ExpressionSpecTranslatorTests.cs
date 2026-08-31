using System.Linq.Expressions;
using System.Reflection;
using FluentSpecifications;
using FluentSpecifications.Expressions;
using Xunit;

namespace FluentSpecifications.Expressions.Tests;

public sealed class ExpressionSpecTranslatorTests
{
    [Fact]
    public void Prepared_expression_preserves_nested_boolean_behavior()
    {
        var paid = Rule("paid", order => order.Paid);
        var priority = Rule("priority", order => order.Priority);
        var manual = Rule("manual", order => order.ManualOverride);
        var suspended = Rule("suspended", order => order.Suspended);
        var specification = paid
            .And(priority.Or(manual))
            .AndNot(suspended)
            .Named("order.can-ship", "Can ship");

        var preparation = new ExpressionSpecTranslator<Order>().Prepare(specification);
        var predicate = preparation.GetPlanOrThrow().Compile();

        Assert.True(predicate(new Order(Paid: true, Priority: true)));
        Assert.True(predicate(new Order(Paid: true, ManualOverride: true)));
        Assert.False(predicate(new Order(Paid: true, Priority: true, Suspended: true)));
        Assert.False(predicate(new Order(Priority: true)));
    }

    [Fact]
    public void Prepared_expression_rebinds_parameters_without_invocation_nodes()
    {
        var paid = Rule("paid", order => order.Paid);
        var priority = Rule("priority", candidate => candidate.Priority);

        var expression = new ExpressionSpecTranslator<Order>()
            .Prepare(paid.And(priority))
            .GetPlanOrThrow();

        Assert.Single(expression.Parameters);
        Assert.False(ContainsInvocation(expression));
    }

    [Fact]
    public void Prepared_expression_preserves_nested_lambda_parameters()
    {
        var hasMatchingTag = Rule(
            "matching-tag",
            order => order.Tags.Any(tag => tag == order.ExpectedTag));
        var paid = Rule("paid", candidate => candidate.Paid);

        var expression = new ExpressionSpecTranslator<Order>()
            .Prepare(paid.And(hasMatchingTag))
            .GetPlanOrThrow();
        var predicate = expression.Compile();

        Assert.True(predicate(new Order(Paid: true, ExpectedTag: "urgent", TagValues: ["urgent"])));
        Assert.False(predicate(new Order(Paid: true, ExpectedTag: "urgent", TagValues: ["normal"])));
        Assert.False(ContainsInvocation(expression));
    }

    [Fact]
    public void Prepared_expression_supports_boolean_constants()
    {
        var translator = new ExpressionSpecTranslator<Order>();

        Assert.True(translator.Prepare(Spec.Always<Order>()).GetPlanOrThrow().Compile()(new Order()));
        Assert.False(translator.Prepare(Spec.Never<Order>()).GetPlanOrThrow().Compile()(new Order()));
    }

    [Fact]
    public void Translator_rejects_a_null_specification()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ExpressionSpecTranslator<Order>().Prepare(null!));
    }

    [Fact]
    public void Public_rule_and_expression_apis_do_not_expose_iqueryable()
    {
        var assemblies = new[]
        {
            typeof(Spec<>).Assembly,
            typeof(ExpressionSpecTranslator<>).Assembly
        };

        var offendingMembers = assemblies
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .SelectMany(PublicApiTypes)
            .Where(ContainsQueryable)
            .ToArray();

        Assert.Empty(offendingMembers);
    }

    private static IEnumerable<Type> PublicApiTypes(Type type)
    {
        yield return type;

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return property.PropertyType;
        }
    }

    private static bool ContainsQueryable(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>))
        {
            return true;
        }

        return type.HasElementType
            ? ContainsQueryable(type.GetElementType()!)
            : type.IsGenericType && type.GetGenericArguments().Any(ContainsQueryable);
    }

    private static bool ContainsInvocation(Expression expression)
    {
        var finder = new InvocationFinder();
        finder.Visit(expression);
        return finder.Found;
    }

    private static Spec<Order> Rule(string id, Expression<Func<Order, bool>> predicate) =>
        Spec.Define($"order.{id}", id, predicate);

    private sealed class InvocationFinder : ExpressionVisitor
    {
        public bool Found { get; private set; }

        protected override Expression VisitInvocation(InvocationExpression node)
        {
            Found = true;
            return base.VisitInvocation(node);
        }
    }

    private sealed record Order(
        bool Paid = false,
        bool Priority = false,
        bool ManualOverride = false,
        bool Suspended = false,
        string ExpectedTag = "",
        IReadOnlyList<string>? TagValues = null)
    {
        public IReadOnlyList<string> Tags { get; } = TagValues ?? [];
    }
}
