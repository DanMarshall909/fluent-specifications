using FluentSpecifications;
using static FluentSpecifications.Generator.IntegrationTests.OrderRules;
using Xunit;

namespace FluentSpecifications.Generator.IntegrationTests;

public sealed class GeneratedDslTests
{
    [Fact]
    public void Zero_argument_rules_compose_without_parentheses()
    {
        var rule = CanShip.And.HighPriority.AndNot.Suspended;

        Assert.True(rule.Matches(new Order(
            Paid: true,
            HasAddress: true,
            HighPriority: true)));
        Assert.False(rule.Matches(new Order(
            Paid: true,
            HasAddress: true,
            HighPriority: true,
            Suspended: true)));
    }

    [Fact]
    public void Parameterized_rules_keep_parentheses_only_for_arguments()
    {
        var rule = CanShip.And.WorthAtLeast(100m);

        Assert.True(rule.Matches(new Order(Paid: true, HasAddress: true, Total: 150m)));
        Assert.False(rule.Matches(new Order(Paid: true, HasAddress: true, Total: 50m)));
    }

    [Fact]
    public void Connector_invocation_supports_explicit_grouping()
    {
        var rule = CanShip.And(HighPriority.Or.ManualOverride);

        Assert.True(rule.Matches(new Order(
            Paid: true,
            HasAddress: true,
            ManualOverride: true)));
        Assert.False(rule.Matches(new Order(Paid: true, HasAddress: true)));
    }

    [Fact]
    public void Exposed_rule_reads_as_a_boolean_domain_property()
    {
        var ready = new Order(Paid: true, HasAddress: true);
        var unpaid = new Order(HasAddress: true);

        Assert.True(ready.CanShip);
        Assert.False(unpaid.CanShip);
    }

    [Fact]
    public void Generated_rule_methods_preserve_optional_parameters()
    {
        var rule = CanShip.And.InRegion();

        Assert.True(rule.Matches(new Order(Paid: true, HasAddress: true, Region: "AU")));
        Assert.False(rule.Matches(new Order(Paid: true, HasAddress: true, Region: "NZ")));
    }

    [Fact]
    public void Generated_zero_argument_rules_are_cached_as_stable_definitions()
    {
        _ = Paid.And.Counted;
        _ = Paid.Or.Counted;

        Assert.Equal(1, OrderRules.CountedAccessCount);
    }

    [Fact]
    public void Readonly_rule_fields_participate_in_connectors_and_domain_exposure()
    {
        var domestic = new Order(Region: "AU");
        var international = new Order(Region: "NZ");
        var rule = Paid.Or.Domestic;

        Assert.True(domestic.Domestic);
        Assert.False(international.Domestic);
        Assert.True(rule.Matches(domestic));
    }

    [Fact]
    public void Params_overloads_and_keyword_parameter_names_survive_generation()
    {
        var order = new Order(
            Paid: true,
            HasAddress: true,
            Total: 150m,
            Region: "AU");
        var rule = CanShip
            .And.InAnyRegion("AU", "NZ")
            .And.RegionNamed(@class: "AU")
            .And.WorthAtLeast(100L);

        Assert.True(rule.Matches(order));
    }
}

public sealed record Order(
    bool Paid = false,
    bool HasAddress = false,
    bool HighPriority = false,
    bool Suspended = false,
    bool ManualOverride = false,
    decimal Total = 0m,
    string Region = "AU");

[SpecificationSet<Order>]
public static partial class OrderRules
{
    private static int _countedAccessCount;

    public static int CountedAccessCount => _countedAccessCount;

    [Expose]
    public static readonly Spec<Order> Domestic =
        Spec.Define<Order>("order.domestic", "Domestic", order => order.Region == "AU");

    public static Spec<Order> Paid =>
        Spec.Define<Order>("order.paid", "Paid", order => order.Paid);

    public static Spec<Order> HasDeliveryAddress =>
        Spec.Define<Order>(
            "order.has-delivery-address",
            "Has delivery address",
            order => order.HasAddress);

    public static Spec<Order> HighPriority =>
        Spec.Define<Order>(
            "order.high-priority",
            "High priority",
            order => order.HighPriority);

    public static Spec<Order> Suspended =>
        Spec.Define<Order>("order.suspended", "Suspended", order => order.Suspended);

    public static Spec<Order> ManualOverride =>
        Spec.Define<Order>(
            "order.manual-override",
            "Manual override",
            order => order.ManualOverride);

    public static Spec<Order> WorthAtLeast(decimal amount) =>
        Spec.Define<Order>(
            "order.worth-at-least",
            "Worth at least",
            order => order.Total >= amount);

    public static Spec<Order> WorthAtLeast(long amount) =>
        Spec.Define<Order>(
            "order.worth-at-least-long",
            "Worth at least",
            order => order.Total >= amount);

    public static Spec<Order> InAnyRegion(params string[] regions) =>
        Spec.Define<Order>(
            "order.in-any-region",
            "In any region",
            order => regions.Contains(order.Region));

    public static Spec<Order> RegionNamed(string @class) =>
        Spec.Define<Order>(
            "order.region-named",
            "Region named",
            order => order.Region == @class);

    public static Spec<Order> InRegion(string region = "AU") =>
        Spec.Define<Order>(
            "order.in-region",
            "In region",
            order => order.Region == region);

    public static Spec<Order> Counted
    {
        get
        {
            Interlocked.Increment(ref _countedAccessCount);
            return Spec.Define<Order>("order.counted", "Counted", _ => true);
        }
    }

    [Expose]
    public static Spec<Order> CanShip =>
        Paid
            .And(HasDeliveryAddress)
            .AndNot(Suspended)
            .Named("order.can-ship", "Can ship");
}
