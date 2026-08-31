# Fluent Specifications

Fluent Specifications is a C# 14 implementation of the Specification Pattern
designed around terse domain language, structured explanations, and repository
boundaries that do not leak `IQueryable`.

Read the polished documentation at
[fluent-spec.danmarshall.dev](https://fluent-spec.danmarshall.dev).

## Readable at the call site

The example application composes an immutable rule without overloaded Boolean
operators:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.ReadyToShip"
public static Spec<Order> ReadyToShip() =>
    CanShip.And.HighPriority.AndNot.Suspended;
```

Important argument-free rules can become opt-in domain properties:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.ShouldDispatch(FluentSpecifications.Examples.OrderFulfilment.Order)"
public static bool ShouldDispatch(Order order)
{
    if (order.CanShip)
    {
        return true;
    }

    return false;
}
```

Routine application code does not need `IsSatisfiedBy`, `Satisfies`, expression
plumbing, or a query provider.

## One named rule tree

Catalog rules retain stable identity, metadata, and their underlying Boolean
structure:

```csharp symbol="P:FluentSpecifications.Examples.OrderFulfilment.OrderRules.CanShip"
[Expose]
public static Spec<Order> CanShip =>
    Paid
        .And(HasDeliveryAddress)
        .AndNot(Suspended)
        .Named(
            "order.can-ship",
            "Can ship",
            "The order is not ready to ship.");
```

The same `Spec<T>` supports short-circuiting in-memory evaluation, complete or
short-circuit diagnostics, safe rendering, provider-neutral traversal, and
infrastructure translation.

## Persistence stays behind the repository

Application repositories accept rules and return materialized answers:

```csharp symbol="T:FluentSpecifications.Examples.OrderFulfilment.IOrderRepository"
public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> ListAsync(
        Spec<Order> specification,
        CancellationToken cancellationToken = default);

    Task<bool> AnyAsync(
        Spec<Order> specification,
        CancellationToken cancellationToken = default);
}
```

The optional relational EF Core adapter preflights translation and materializes
`List`, `Any`, or `Count` operations. Unsupported filters produce structured
translation errors before a `SELECT`; they never trigger implicit client-side
filtering. Its public API does not accept or return `IQueryable`.

Read the [EF Core guide](https://fluent-spec.danmarshall.dev/docs/ef-core/) for
null semantics, collations, navigations, global filters, provider limitations,
and the limits of SQLite-based testing.

## Projects

- `FluentSpecifications.Core` — immutable rule tree, evaluation, diagnostics,
  traversal, and translation contracts.
- `FluentSpecifications.Generators` — C# 14 connector and domain extension
  properties plus compile-time diagnostics.
- `FluentSpecifications.Expressions` — parameter-rebound expression plans
  without `InvocationExpression`.
- `FluentSpecifications.EntityFrameworkCore` — relational translation
  preflight and materializing operations for infrastructure.
- `OrderFulfilment` — the executable domain example used throughout the tests
  and documentation.
- `FluentSpecifications.Docs` — Roslyn-based extraction of real source symbols
  into Markdown and the Astro landing page.

## Documentation that cannot drift quietly

Every fenced C# sample in the documentation carries a canonical Roslyn
documentation ID in its fence metadata. `npm run snippets:sync` resolves those
symbols—including overload signatures—and regenerates the fence bodies from
the repository. Missing, ambiguous, or stale extracts fail
`npm run snippets:check`.

The documentation site is authored as Markdown under `src/content/docs`, built
with Astro into `docs/`, and published by GitHub Actions under
`gh-pages:/docs`. Both `CNAME` copies target `fluent-spec.danmarshall.dev`,
matching the deployment style used by Dan's blog.

## Build and test

Restore with `dotnet restore FluentSpecifications.slnx` and `npm ci`.

Run the complete .NET suite with `dotnet test FluentSpecifications.slnx
--configuration Release --no-restore -m:1 -nr:false`. Run `npm test` to verify
snippet freshness, Markdown contracts, the production Astro build, metadata,
custom-domain artifacts, and internal links.

NuGet packaging and publication are not yet complete. The checked-in
[SPECIFICATION.md](SPECIFICATION.md) and executable tests are the current
version-one contract.
