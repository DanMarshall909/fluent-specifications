# Fluent Specifications

**Specifications for modern C#.**

Fluent Specifications is a C# 14 implementation of the Specification Pattern
designed around terse domain language, structured explanations, and repository
boundaries that do not leak `IQueryable`.

Read the polished documentation at
[fluent-spec.danmarshall.dev](https://fluent-spec.danmarshall.dev).

## Install

```shell
dotnet add package DanMarshall.FluentSpecifications
```

One package installs both the `Spec<T>` runtime and the source generator that
produces fluent connectors such as `.And.Paid` and opt-in domain properties
such as `order.CanShip`.

### Zero third-party package dependencies

The package has **zero third-party package dependencies**. Its NuGet dependency
list is empty: the runtime uses the .NET platform, and the generator uses the
Roslyn compiler APIs supplied by Microsoft's C# toolchain. It neither downloads
nor bundles another vendor's runtime or compiler assemblies.

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

## Prior art and acknowledgements

Fluent Specifications is informed by the original Specification pattern and by
practical lessons from several libraries and policy systems:

- [Ardalis.Specification](https://github.com/ardalis/Specification), created by
  Steve Smith, demonstrated reusable named specifications and small repository
  surfaces;
- [Spring Data JPA Specifications](https://docs.spring.io/spring-data/jpa/reference/jpa/specifications.html)
  keeps criteria separate from repository execution;
- [RulerZ](https://github.com/K-Phoen/rulerz) demonstrates a provider-neutral
  rule model compiled for different targets;
- [Happyr Doctrine Specification](https://github.com/Happyr/Doctrine-Specification)
  illustrates both repository-owned application and the tension created when
  Boolean rules also carry query modifiers;
- [Konform](https://github.com/konform-kt/konform),
  [NSpecifications](https://github.com/miholler/NSpecifications), and
  [spec-pattern](https://github.com/thiagodp/spec-pattern) informed structured
  results, expression composition, and explicit composite trees;
- [Cedar](https://github.com/cedar-policy/cedar) and
  [Open Policy Agent](https://github.com/open-policy-agent/opa) informed stable
  rule identity, explicit errors, preparation, and traceability; and
- an internal Kotlin implementation Dan used while working at Reapit showed
  how much terse, fluent domain language matters at ordinary call sites.

These are influences, not compatibility targets. Fluent Specifications keeps a
deliberately narrower Boolean core and does not copy their APIs.

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

`DanMarshall.FluentSpecifications` 1.x is the public starter package. Releases
begin at 1.0.0, and each push to `main` receives the next patch version. The
checked-in [SPECIFICATION.md](SPECIFICATION.md), package-consumer tests, and
executable conformance suites define its version-one contract.
