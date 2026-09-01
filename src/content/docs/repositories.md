---
title: Repository extension
description: Use the optional provider-neutral read repository contract, choose an EF Core implementation or another provider, and preserve a materialized application boundary.
order: 5
section: Infrastructure
---

`FluentSpecifications.Repositories` adds a small generic read boundary around
specifications and searches. It standardizes the operations that providers have
in common without making EF Core, `DbContext`, `IQueryable`, or another storage
API part of application code.

The extension is deliberately read-only. Writes, units of work, transactions,
includes, projections, and provider options remain application or
infrastructure concerns.

## Availability

The repository contract is published separately from the starter package,
beginning with the coordinated 1.2.0 package suite:

```shell
dotnet add package DanMarshall.FluentSpecifications.Repositories --version 1.2.0
```

`DanMarshall.FluentSpecifications.Repositories` depends only on
`DanMarshall.FluentSpecifications`; the starter package does not transitively
include the repository, expression, or EF Core extensions. This keeps a domain
model that only needs `Spec<T>` free of persistence abstractions.

The dependency supplies the core runtime. Add the starter package directly when
the consuming project also needs its source generator; analyzer assets do not
flow transitively through an extension package.

The contract uses the root `FluentSpecifications` namespace. Contributors
working inside this repository can use the matching
`FluentSpecifications.Repositories` project reference instead of the package.

## Use the generic contract directly or specialize it

Application services can depend directly on `IReadRepository<Order>`. A domain
can also give that dependency a more specific name while inheriting the same
read surface:

```csharp symbol="T:FluentSpecifications.Examples.OrderFulfilment.IOrderRepository"
public interface IOrderRepository : IReadRepository<Order>
{
}
```

Use a domain-specific interface when it adds useful domain operations or makes
an architectural boundary clearer. Do not create one only to copy every generic
method by hand.

The generic contract keeps ordinary list operations materialized:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.FindReadyOrdersAsync(FluentSpecifications.IReadRepository{FluentSpecifications.Examples.OrderFulfilment.Order},System.Threading.CancellationToken)"
public static Task<IReadOnlyList<Order>> FindReadyOrdersAsync(
    IReadRepository<Order> repository,
    CancellationToken cancellationToken = default) =>
    repository.ListAsync(
        CanShip.And(HighPriority.Or.ManualOverride),
        cancellationToken);
```

A paged search crosses the same boundary and returns its metadata with the
results:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.FindPriorityOrdersAsync(FluentSpecifications.IReadRepository{FluentSpecifications.Examples.OrderFulfilment.Order},System.Threading.CancellationToken)"
public static Task<Page<Order>> FindPriorityOrdersAsync(
    IReadRepository<Order> repository,
    CancellationToken cancellationToken = default) =>
    repository.PageAsync(
        PriorityShippingPage(),
        cancellationToken);
```

No application method receives a deferred query or provider expression.

## Operations and semantics

| Operation | Input | Materialized result | Search shaping |
| --- | --- | --- | --- |
| `ListAsync` | `Spec<T>` | `IReadOnlyList<T>` | Filter only |
| `ListAsync` | `Search<T>` | `IReadOnlyList<T>` | Filter, ordering, and optional paging |
| `PageAsync` | `PagedSearch<T>` | `Page<T>` | Filter, ordering, paging, and total count |
| `AnyAsync` | `Spec<T>` or `Search<T>` | `bool` | Filter only |
| `CountAsync` | `Spec<T>` or `Search<T>` | `int` | Filter only |

`AnyAsync(Search<T>)` and `CountAsync(Search<T>)` use the search specification
but do not let ordering or paging change the aggregate. `PageAsync` counts all
filtered results before applying the requested page and returns that count as
`Page<T>.TotalResults`.

Every operation accepts a `CancellationToken`. Implementations should observe
an already-cancelled token before translation or I/O and pass it through to the
underlying provider.

## EF Core is one implementation

The EF adapter exposes `EntityFrameworkRepository<T>`, which implements
`IReadRepository<T>` for mapped reference types:

```csharp symbol="M:FluentSpecifications.EntityFrameworkCore.Tests.OrderFulfilmentExamples.Repository_example_executes_the_same_rule_in_sqlite"
[Fact]
public async Task Repository_example_executes_the_same_rule_in_sqlite()
{
    await using var database = await ExampleDatabase.CreateAsync();
    IReadRepository<Order> repository =
        new EntityFrameworkRepository<Order>(database.Context);
    var ready = CanShip.And(HighPriority.Or.ManualOverride).AndNot.Suspended;

    var orders = await repository.ListAsync(ready);

    Assert.Equal([1, 2], orders.Select(order => order.Id).Order().ToArray());
}
```

That implementation delegates provider work to the relational executor. It
preflights translation, keeps global query filters active, materializes with
no tracking, and rejects unsupported filters or sorts instead of silently
switching to client evaluation.

The `where T : class` constraint belongs to the EF implementation, not the
generic contract. Read the [EF Core guide](/docs/ef-core/) for null semantics,
collations, navigations, provider limits, and database-level testing.

## Implement another provider

Any provider can implement `IReadRepository<T>` without referencing EF Core.
The contract test suite proves this with an in-memory provider over a value
type:

```csharp symbol="M:FluentSpecifications.Repositories.Tests.RepositoryContractTests.A_non_ef_provider_can_implement_the_contract_for_value_types"
[Fact]
public async Task A_non_ef_provider_can_implement_the_contract_for_value_types()
{
    IReadRepository<int> repository =
        new InMemoryReadRepository<int>([1, 2, 3, 4]);
    var even = Spec.Define<int>("number.even", "Even", number => number % 2 == 0);
    var search = Search.Matching(even)
        .Sorted.By[SearchField.Define<int, int>("Value", number => number)].Desc
        .Page(1).OfSize(1);

    Assert.Equal([2, 4], await repository.ListAsync(even));
    Assert.Equal([4], (await repository.PageAsync(search)).Results);
    Assert.True(await repository.AnyAsync(even));
    Assert.Equal(2, await repository.CountAsync(even));
}
```

A database, HTTP API, document store, search service, or test provider should:

- evaluate or translate the complete `Spec<T>` filter;
- apply search ordering in the declared order;
- apply paging only where the operation requires it;
- return fully materialized results;
- preserve cancellation;
- report unsupported behavior explicitly; and
- keep connection, query, and provider configuration behind the implementation.

An implementation must not expose `IQueryable`, fetch an unbounded data set as
an implicit fallback, or silently discard a rule or sort that it cannot execute.

## Keep writes domain-specific

`IReadRepository<T>` does not prescribe `Add`, `Update`, `Delete`, `Save`, or a
unit of work. A domain repository can inherit the read contract and add the
commands that its aggregate actually supports, or it can keep command handlers
separate. Either choice is preferable to pretending every data source has the
same generic mutation semantics.

The [testing guide](/docs/testing/) covers repository conformance evidence. The
[reference](/docs/reference/) summarizes package ownership and the broader
non-goals.
