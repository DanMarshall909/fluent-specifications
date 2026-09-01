---
title: Getting started
description: Define a small catalog of named rules, compose them into fluent business language, and evaluate the result without exposing persistence details.
order: 1
section: Start here
---

Fluent Specifications is built around one idea: a business rule should be easy
to name, combine, run, and explain without becoming a query object.

## Install

Install the .NET 10 SDK, open a terminal in the directory containing your
project file, and run:

```shell
dotnet add package DanMarshall.FluentSpecifications
```

That adds the latest stable `DanMarshall.FluentSpecifications` release to the
project. Pin `--version 1.0.0` when you specifically need the initial release.
If you edit project files directly, the equivalent is a `PackageReference`
with that ID and version. Add `using FluentSpecifications;` (or a global
using), then create a `public static partial` rule catalog marked with
`[SpecificationSet<T>]` as shown below. The first build runs the included source
generator automatically.

The package contains both the runtime and source generator and has **zero
third-party package dependencies**. It relies only on .NET and compiler APIs
supplied by Microsoft, and it does not bundle vendor runtime or compiler
assemblies.

## The smallest useful example

Create a static partial catalog for the domain type. A rule has a stable ID, a
human name, and a typed predicate:

```csharp symbol="T:FluentSpecifications.Examples.OrderFulfilment.QuickStartRules"
[SpecificationSet<QuickStartOrder>]
public static partial class QuickStartRules
{
    public static Spec<QuickStartOrder> Paid =>
        Spec.Define<QuickStartOrder>(
            "order.paid",
            "Paid",
            order => order.Paid);

    public static Spec<QuickStartOrder> Priority =>
        Spec.Define<QuickStartOrder>(
            "order.priority",
            "Priority",
            order => order.Priority);
}
```

The faint parameter labels shown in documentation examples are generated from
the same Roslyn model that extracts the source. They are visual aids, like
Rider inlay hints; they are not part of the copied C#.

Import the catalog once, then compose rules without operator overloads:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.ReadyToShip"
public static Spec<Order> ReadyToShip() =>
    CanShip.And.HighPriority.AndNot.Suspended;
```

The returned value is still a `Spec<Order>`. It can be reused, rendered,
diagnosed, or passed to infrastructure without losing its rule tree.

## Filter, sort, and page without a query API

When a repository needs more than a Boolean filter, start a separate immutable
search from the entity type. Search generation is opt-in: set
`GenerateSearch = true` on the entity's `SpecificationSet<T>` catalog. The
target entity then lets the generator infer both the rule and field catalogs:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.PriorityShippingPage|local:request"
var request = Order.Search
    .Matching.CanShip.And.HighPriority
    .Sorted.By.CreatedAt.Desc
    .Then.By.Id.Asc
    .Page(2).OfSize(50);
```

`Order` is the entity—not a `DbSet`. `Order.Search` only creates a
provider-neutral description. A repository materializes it; application code
never receives `IQueryable`. `Order.Rules` and `Order.Fields` remain available
when a rule or field needs to be selected dynamically.

Use the optional [`FluentSpecifications.Repositories`](/docs/repositories/)
project when multiple providers should share the materializing
`IReadRepository<T>` contract. EF Core is one implementation of that contract,
not a dependency of it.

## Use a rule as domain language

Mark a zero-argument rule with `[Expose]` when it deserves to read like a
Boolean property on the domain object:

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

The generator supplies the domain property:

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

`[Expose]` is opt-in. It is best for important, argument-free domain concepts,
not every small leaf rule.

## What the package contains

`DanMarshall.FluentSpecifications` contains the immutable rule tree,
evaluation and diagnostic APIs, plus the source generator that creates the
fluent connector members. The generator runs as a compiler analyzer and is not
a runtime dependency.

Provider translation remains deliberately separate. The repository contains
the provider-neutral repository contract plus expression and EF Core adapters,
but the starter package does not pull EF Core or expose `IQueryable` to
application code. Filtering, sorting, and paging are described in the starter
package and executed only by infrastructure.

## Where to go next

Read [defining rules](/docs/defining-rules/) for naming and catalog design, then
[composition](/docs/composition/) for grouping, negation, and parameterized
rules. Read the [repository guide](/docs/repositories/) before choosing a
persistence boundary. If the rules will reach a relational database, read the
[EF Core guide](/docs/ef-core/) before assuming an in-memory match will
translate. The [prior-art notes](/docs/prior-art/) explain the design lineage
and where Fluent Specifications deliberately differs.
