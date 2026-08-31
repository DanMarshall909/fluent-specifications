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

    [Fact]
    public void Search_rules_and_fields_are_inferred_from_the_entity()
    {
        var request = Order.Search
            .Matching.CanShip.And.HighPriority
            .Sorted.By.CreatedAt.Desc
            .Then.By.Id.Asc
            .Page(2).OfSize(50);

        Assert.Equal("Can ship AND High priority", request.Specification.ToString());
        Assert.Equal("CreatedAt", request.Ordering[0].Field.Name);
        Assert.Equal(SearchSortDirection.Descending, request.Ordering[0].Direction);
        Assert.Equal("Id", request.Ordering[1].Field.Name);
        Assert.Equal(SearchSortDirection.Ascending, request.Ordering[1].Direction);
        Assert.Equal(2, request.Paging!.Number);
        Assert.Equal(50, request.Paging.Size);
    }

    [Fact]
    public void Explicit_rule_and_field_catalogs_are_available_without_partial_entities()
    {
        var rule = Order.Rules.CanShip.And.HighPriority;
        var request = Order.Search
            .For(rule)
            .Sorted.By[Order.Fields.CreatedAt].Desc
            .Then.By[Order.Fields.Id].Asc;

        Assert.True(rule.Matches(new Order(Paid: true, HasAddress: true, HighPriority: true)));
        Assert.Equal(["CreatedAt", "Id"], request.Ordering.Select(item => item.Field.Name));
    }

    [Fact]
    public void Search_all_is_an_explicit_unfiltered_start()
    {
        var request = Order.Search.All.Sorted.By.Id.Asc;

        Assert.True(request.Specification.Matches(new Order()));
    }

    [Fact]
    public void Inherited_readable_fields_are_available_to_search_ordering()
    {
        var request = Order.Search.All.Sorted.By.TenantId.Asc;

        Assert.Equal("TenantId", request.Ordering[0].Field.Name);
    }

    [Fact]
    public void Fields_named_like_the_dynamic_selector_are_available_to_search_ordering()
    {
        var shorthand = Order.Search.All.Sorted.By.Field.Asc;
        var dynamic = Order.Search.All.Sorted.By[Order.Fields.Field].Desc;

        Assert.Equal("Field", shorthand.Ordering[0].Field.Name);
        Assert.Equal("Field", dynamic.Ordering[0].Field.Name);
    }
}

public abstract record Entity
{
    public int TenantId { get; init; }
}

public sealed record Order(
    bool Paid = false,
    bool HasAddress = false,
    bool HighPriority = false,
    bool Suspended = false,
    bool ManualOverride = false,
    decimal Total = 0m,
    string Region = "AU",
    int Id = 0,
    DateTime CreatedAt = default,
    int Field = 0) : Entity;

[SpecificationSet<Order>(GenerateSearch = true)]
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
