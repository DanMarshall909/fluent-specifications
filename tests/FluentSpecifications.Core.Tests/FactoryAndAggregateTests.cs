using System.Collections;
using System.Linq.Expressions;
using FluentSpecifications;
using Xunit;

namespace FluentSpecifications.Core.Tests;

public sealed class FactoryAndAggregateTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Define_rejects_a_blank_id(string id)
    {
        Assert.Throws<ArgumentException>(() =>
            Spec.Define<Order>(id, "Paid", order => order.Paid));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Define_rejects_a_blank_name(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            Spec.Define<Order>("order.paid", name, order => order.Paid));
    }

    [Fact]
    public void Define_rejects_a_null_predicate()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Spec.Define<Order>(
                "order.paid",
                "Paid",
                (Expression<Func<Order, bool>>)null!));
    }

    [Fact]
    public void Connector_rejects_a_null_right_hand_rule()
    {
        var paid = Rule("paid", order => order.Paid);

        Assert.Throws<ArgumentNullException>(() => paid.And(null!));
    }

    [Fact]
    public void Always_and_never_are_explicit_constants()
    {
        Assert.True(Spec.Always<Order>().Matches(new Order()));
        Assert.False(Spec.Never<Order>().Matches(new Order()));
    }

    [Fact]
    public void Empty_aggregates_follow_boolean_identity_laws()
    {
        Assert.True(Spec.AllOf(Array.Empty<Spec<Order>>()).Matches(new Order()));
        Assert.False(Spec.AnyOf(Array.Empty<Spec<Order>>()).Matches(new Order()));
    }

    [Fact]
    public void AllOf_requires_every_rule_and_AnyOf_requires_one_rule()
    {
        var paid = Rule("paid", order => order.Paid);
        var priority = Rule("priority", order => order.Priority);

        var all = Spec.AllOf([paid, priority]);
        var any = Spec.AnyOf([paid, priority]);

        Assert.True(all.Matches(new Order(Paid: true, Priority: true)));
        Assert.False(all.Matches(new Order(Paid: true)));
        Assert.True(any.Matches(new Order(Priority: true)));
        Assert.False(any.Matches(new Order()));
    }

    [Fact]
    public void Aggregates_snapshot_the_source_in_one_enumeration()
    {
        var source = new SingleEnumeration<Spec<Order>>(
            Rule("paid", order => order.Paid),
            Rule("priority", order => order.Priority));

        var all = Spec.AllOf(source);

        Assert.Equal(1, source.EnumerationCount);
        Assert.True(all.Matches(new Order(Paid: true, Priority: true)));
        Assert.Equal(1, source.EnumerationCount);
    }

    [Fact]
    public void Aggregates_reject_null_elements()
    {
        Spec<Order>[] rules = [Rule("paid", order => order.Paid), null!];

        Assert.Throws<ArgumentException>(() => Spec.AnyOf(rules));
    }

    [Fact]
    public void Double_negation_preserves_the_original_behavior()
    {
        var paid = Rule("paid", order => order.Paid);

        Assert.True(paid.Not.Not.Matches(new Order(Paid: true)));
        Assert.False(paid.Not.Not.Matches(new Order()));
    }

    [Fact]
    public void Explicitly_nullable_candidates_are_decided_by_the_rule()
    {
        var missing = Spec.Define<Order?>(
            "order.missing",
            "Missing",
            order => order == null);

        Assert.True(missing.Matches(null));
        Assert.True(missing.Check(null).Passed);
    }

    private static Spec<Order> Rule(string name, Expression<Func<Order, bool>> predicate) =>
        Spec.Define($"order.{name}", name, predicate);

    private sealed record Order(bool Paid = false, bool Priority = false);

    private sealed class SingleEnumeration<T>(params T[] values) : IEnumerable<T>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("The sequence was enumerated more than once.");
            }

            return ((IEnumerable<T>)values).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
