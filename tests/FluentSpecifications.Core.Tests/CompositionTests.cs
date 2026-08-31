using System.Linq.Expressions;
using FluentSpecifications;
using Xunit;

namespace FluentSpecifications.Core.Tests;

public sealed class CompositionTests
{
    [Fact]
    public void Define_creates_a_rule_that_matches_its_predicate()
    {
        var paid = Spec.Define<Order>(
            id: "order.paid",
            name: "Paid",
            predicate: order => order.Paid);

        Assert.True(paid.Matches(new Order(Paid: true)));
        Assert.False(paid.Matches(new Order(Paid: false)));
    }

    [Fact]
    public void Connectors_compose_rules_with_boolean_semantics()
    {
        var paid = Rule("paid", order => order.Paid);
        var priority = Rule("priority", order => order.Priority);
        var suspended = Rule("suspended", order => order.Suspended);

        var rule = paid.And(priority.Or(suspended)).AndNot(suspended);

        Assert.True(rule.Matches(new Order(Paid: true, Priority: true)));
        Assert.False(rule.Matches(new Order(Paid: true, Suspended: true)));
        Assert.False(rule.Matches(new Order(Priority: true)));
    }

    [Fact]
    public void Matches_short_circuits_and_from_left_to_right()
    {
        var probe = new EvaluationProbe();
        var left = Rule("left", _ => false);
        var right = Rule("right", _ => probe.Return(true));

        Assert.False(left.And(right).Matches(new Order()));
        Assert.False(probe.WasEvaluated);
    }

    [Fact]
    public void Matches_short_circuits_or_from_left_to_right()
    {
        var probe = new EvaluationProbe();
        var left = Rule("left", _ => true);
        var right = Rule("right", _ => probe.Return(false));

        Assert.True(left.Or(right).Matches(new Order()));
        Assert.False(probe.WasEvaluated);
    }

    [Fact]
    public void Not_inverts_a_normal_result()
    {
        var paid = Rule("paid", order => order.Paid);

        Assert.False(paid.Not.Matches(new Order(Paid: true)));
        Assert.True(paid.Not.Matches(new Order(Paid: false)));
    }

    private static Spec<Order> Rule(string name, Expression<Func<Order, bool>> predicate) =>
        Spec.Define(
            id: $"order.{name}",
            name: name,
            predicate: predicate);

    private sealed record Order(
        bool Paid = false,
        bool Priority = false,
        bool Suspended = false);

    private sealed class EvaluationProbe
    {
        public bool WasEvaluated { get; private set; }

        public bool Return(bool result)
        {
            WasEvaluated = true;
            return result;
        }
    }
}
