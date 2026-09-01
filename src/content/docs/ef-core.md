---
title: Entity Framework Core
description: Keep IQueryable inside infrastructure, preflight relational translation, and account explicitly for nulls, collations, navigations, filters, and provider limits.
order: 5
section: Infrastructure
---

EF Core is one implementation of the provider-neutral repository boundary.
Applications pass Boolean rules or immutable searches to repositories; they do
not receive deferred queries, provider expressions, or EF configuration.

## The contract is not tied to EF

The optional repository extension exposes the small, read-only
`IReadRepository<T>` contract with no provider dependency or reference-type
constraint. Applications can use it directly or specialize it with a domain
name:

```csharp symbol="T:FluentSpecifications.Examples.OrderFulfilment.IOrderRepository"
public interface IOrderRepository : IReadRepository<Order>
{
}
```

The generic contract provides materializing `ListAsync`, `PageAsync`,
`AnyAsync`, and `CountAsync` operations for specifications and searches. An
application can also define its own repository shape. EF Core's
`EntityFrameworkRepository<T>` implements the shared contract; other providers
can implement it without inheriting EF's mapped-class constraint.

Application code supplies a specification and receives materialized data:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.FindReadyOrdersAsync(FluentSpecifications.IReadRepository{FluentSpecifications.Examples.OrderFulfilment.Order},System.Threading.CancellationToken)"
public static Task<IReadOnlyList<Order>> FindReadyOrdersAsync(
    IReadRepository<Order> repository,
    CancellationToken cancellationToken = default) =>
    repository.ListAsync(
        CanShip.And(HighPriority.Or.ManualOverride),
        cancellationToken);
```

Sorting and paging remain outside the Boolean rule but can travel in a separate,
provider-neutral search description:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.PriorityShippingPage|local:request"
var request = Order.Search
    .Matching.CanShip.And.HighPriority
    .Sorted.By.CreatedAt.Desc
    .Then.By.Id.Asc
    .Page(2).OfSize(50);
```

Projection, includes, tracking, split queries, provider functions, and cache
policy remain infrastructure concerns.

## Translation is checked before execution

The EF implementation composes the complete expression and asks the configured
provider to translate it before executing a command. An unsupported filter
produces structured rule and tree-path errors; it does not fetch an unbounded
set and retry in memory:

```csharp symbol="M:FluentSpecifications.EntityFrameworkCore.Tests.OrderFulfilmentExamples.Unsupported_filter_fails_before_any_select_is_executed"
[Fact]
public async Task Unsupported_filter_fails_before_any_select_is_executed()
{
    await using var database = await ExampleDatabase.CreateAsync();
    database.CommandCounter.Reset();
    var repository = new EntityFrameworkRepository<Order>(database.Context);
    var inMemoryCandidate = new Order { CustomerName = "ALICE" };
    var rule = CustomerNamedIgnoringCase("alice");

    Assert.True(rule.Matches(inMemoryCandidate));

    var exception = await Assert.ThrowsAsync<SpecificationTranslationException>(() =>
        repository.ListAsync(rule));

    var error = Assert.Single(exception.Errors);
    Assert.Equal("ef-core-translation-failed", error.Code);
    Assert.Equal("order.customer-named-ignoring-case", error.RuleId);
    Assert.Equal("$", error.NodePath);
    Assert.Equal(0, database.CommandCounter.ReaderExecutions);
}
```

Modern EF Core also rejects untranslatable filter expressions rather than
silently evaluating them on the client. Only the top-level projection permits
limited client evaluation. See Microsoft's [client versus server evaluation](https://learn.microsoft.com/en-us/ef/core/querying/client-eval)
guidance.

`ListAsync`, `PageAsync`, `AnyAsync`, and `CountAsync` return materialized
answers. A paged search is fully preflighted before the adapter counts or loads
rows; its count ignores sorting and paging and becomes `Page<T>.TotalResults`.
The adapter's exported API is guarded against accidentally accepting or
returning `IQueryable`:

```csharp symbol="M:FluentSpecifications.EntityFrameworkCore.Tests.OrderFulfilmentExamples.Public_ef_adapter_api_never_returns_or_accepts_iqueryable"
[Fact]
public void Public_ef_adapter_api_never_returns_or_accepts_iqueryable()
{
    var offendingTypes = typeof(RelationalSpecExecutor<>).Assembly
        .GetExportedTypes()
        .SelectMany(PublicApiTypes)
        .Where(ContainsQueryable)
        .ToArray();

    Assert.Empty(offendingTypes);
}
```

## In-memory success is not translation proof

A domain method or a `StringComparison` overload can work perfectly through
`Matches` and still be unsupported by the provider. Treat translation as a
capability of a particular provider, model, and EF version—not of the C#
expression in isolation.

Captured rule arguments should become database parameters. Do not read mutable
ambient state or clocks from inside a predicate.

## Null semantics can diverge

EF normally adds SQL compensation so nullable comparisons behave more like CLR
two-valued logic. The example proves parity for nullable inequality under the
default mode:

```csharp symbol="M:FluentSpecifications.EntityFrameworkCore.Tests.OrderFulfilmentExamples.Null_inequality_has_matching_clr_and_default_ef_semantics"
[Fact]
public async Task Null_inequality_has_matching_clr_and_default_ef_semantics()
{
    await using var database = await ExampleDatabase.CreateAsync();
    var repository = new EntityFrameworkRepository<Order>(database.Context);
    var rule = CustomerReferenceIsNot("BLOCKED");
    var inMemoryIds = database.VisibleSeedOrders
        .Where(rule.Matches)
        .Select(order => order.Id)
        .Order()
        .ToArray();

    var databaseIds = (await repository.ListAsync(rule))
        .Select(order => order.Id)
        .Order()
        .ToArray();

    Assert.Equal(inMemoryIds, databaseIds);
    Assert.Contains(1, databaseIds); // null != "BLOCKED" under compensated semantics
    Assert.DoesNotContain(4, databaseIds);
}
```

Enabling relational null semantics deliberately changes that result. Read
[EF Core query null semantics](https://learn.microsoft.com/en-us/ef/core/querying/null-comparisons)
before changing the option, and test the exact predicates your application
depends on.

## Strings belong to the database collation

Case and accent behavior comes from the column or database collation. EF does
not translate `string.Equals` overloads that take `StringComparison`, because
it cannot infer an appropriate collation. Calling `ToLower` to force equality
can also prevent index use. See [collations and case sensitivity](https://learn.microsoft.com/en-us/ef/core/miscellaneous/collations-and-case-sensitivity).

Tests should state the provider-specific result rather than naming it as a
universal string rule.

## Navigations do not imply Include

Guard optional navigations explicitly when the rule must behave safely in
memory. SQL translation may null-propagate an unsafe-looking navigation access,
which can otherwise create a difference between CLR and database behavior.

A navigation predicate filters; it does not request eager loading. The example
verifies that the related customer is not populated merely because the rule
mentioned it, and that list materialization is no-tracking.

## Global filters still apply

`Always<T>()` means the specification adds no restriction. It does not bypass
tenant, soft-delete, or other EF model filters. Repositories should never rely
on a specification to neutralize those safety boundaries.

## Provider limits are real

SQLite is a useful relational test provider, but it has scalar limitations. In
particular, ordering and comparison for types including `DateTimeOffset`,
`decimal`, and `TimeSpan` can be unsupported. See the official [SQLite provider
limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations).

The adapter turns the demonstrated `DateTimeOffset` comparison failure into a
structured translation error:

```csharp symbol="M:FluentSpecifications.EntityFrameworkCore.Tests.OrderFulfilmentExamples.Provider_specific_scalar_limit_is_a_structured_translation_error"
[Fact]
public async Task Provider_specific_scalar_limit_is_a_structured_translation_error()
{
    await using var database = await ExampleDatabase.CreateAsync();
    var repository = new EntityFrameworkRepository<Order>(database.Context);
    var rule = ProviderTimestampBefore(DateTimeOffset.UtcNow);

    var exception = await Assert.ThrowsAsync<SpecificationTranslationException>(() =>
        repository.ListAsync(rule));

    var error = Assert.Single(exception.Errors);
    Assert.Equal("order.provider-timestamp-before", error.RuleId);
    Assert.Equal("$", error.NodePath);
}
```

## What the SQLite suite does not prove

SQLite in-memory exercises a real relational translator and database. It does
not establish SQL Server, PostgreSQL, or production-schema conformance. EF's
InMemory provider is used here only to prove that the relational adapter rejects
a non-relational context—not to validate query semantics.

Microsoft recommends testing important queries against the actual production
database and discourages the InMemory provider as a query fake. See [choosing a
testing strategy](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy)
and the [provider matrix](https://learn.microsoft.com/en-us/ef/core/providers/).
