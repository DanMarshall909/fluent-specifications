using System.Linq.Expressions;
using FluentSpecifications;
using Xunit;

namespace FluentSpecifications.Core.Tests;

public sealed class RenderingTests
{
    [Fact]
    public void Leaf_renders_its_domain_name_without_expression_values()
    {
        var minimum = 100m;
        var valuable = Spec.Define<Order>(
            "order.valuable",
            "Worth at least",
            order => order.Total >= minimum);

        Assert.Equal("Worth at least", valuable.ToString());
        Assert.DoesNotContain("100", valuable.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rendering_preserves_grouping_and_uses_words_for_negation()
    {
        var canShip = Rule("can-ship", "Can ship", order => order.CanShip);
        var priority = Rule("priority", "High priority", order => order.Priority);
        var manual = Rule("manual", "Manual override", order => order.ManualOverride);
        var suspended = Rule("suspended", "Suspended", order => order.Suspended);

        var rule = canShip.And(priority.Or(manual)).AndNot(suspended);

        Assert.Equal(
            "Can ship AND (High priority OR Manual override) AND NOT Suspended",
            rule.ToString());
    }

    [Fact]
    public void Named_renders_as_a_domain_boundary_but_preserves_behavior()
    {
        var paid = Rule("paid", "Paid", order => order.Paid);
        var addressed = Rule("addressed", "Has delivery address", order => order.HasAddress);

        var canShip = paid.And(addressed).Named(
            id: "order.can-ship",
            name: "Can ship",
            failure: "The order is not ready to ship.");

        Assert.Equal("Can ship", canShip.ToString());
        Assert.True(canShip.Matches(new Order(Paid: true, HasAddress: true)));
        Assert.False(canShip.Matches(new Order(Paid: true)));
    }

    [Fact]
    public void Constants_have_safe_stable_rendering()
    {
        Assert.Equal("Always", Spec.Always<Order>().ToString());
        Assert.Equal("Never", Spec.Never<Order>().ToString());
    }

    [Fact]
    public void Named_rejects_blank_identity()
    {
        var paid = Rule("paid", "Paid", order => order.Paid);

        Assert.Throws<ArgumentException>(() => paid.Named("", "Can ship"));
        Assert.Throws<ArgumentException>(() => paid.Named("order.can-ship", " "));
    }

    private static Spec<Order> Rule(
        string id,
        string name,
        Expression<Func<Order, bool>> predicate) =>
        Spec.Define($"order.{id}", name, predicate);

    private sealed record Order(
        bool Paid = false,
        bool HasAddress = false,
        bool CanShip = false,
        bool Priority = false,
        bool ManualOverride = false,
        bool Suspended = false,
        decimal Total = 0m);
}
