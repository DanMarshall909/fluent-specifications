---
title: Getting started
description: Define a small catalog of named rules, compose them into fluent business language, and evaluate the result without exposing persistence details.
order: 1
section: Start here
---

Fluent Specifications is built around one idea: a business rule should be easy
to name, combine, run, and explain without becoming a query object.

## The smallest useful example

Create a static partial catalog for the domain type. A rule has a stable ID, a
human name, and a typed predicate:

```csharp symbol="P:FluentSpecifications.Examples.OrderFulfilment.OrderRules.Paid"
public static Spec<Order> Paid =>
    Spec.Define<Order>("order.paid", "Paid", order => order.Paid);
```

Import the catalog once, then compose rules without operator overloads:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.ShippingExamples.ReadyToShip"
public static Spec<Order> ReadyToShip() =>
    CanShip.And.HighPriority.AndNot.Suspended;
```

The returned value is still a `Spec<Order>`. It can be reused, rendered,
diagnosed, or passed to infrastructure without losing its rule tree.

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

## Project shape

The repository currently contains project packages rather than published NuGet
artifacts:

- `FluentSpecifications.Core` owns the immutable rule tree and evaluation.
- `FluentSpecifications.Generators` creates the fluent connector members.
- `FluentSpecifications.Expressions` prepares provider-neutral expressions.
- `FluentSpecifications.EntityFrameworkCore` translates and materializes
  relational EF Core queries inside infrastructure.

The generator is referenced as an analyzer project today. Published package
references are intentionally deferred until the API and package split are
ready to version together.

## Where to go next

Read [defining rules](/docs/defining-rules/) for naming and catalog design, then
[composition](/docs/composition/) for grouping, negation, and parameterized
rules. If the rules will reach a database, read the [EF Core guide](/docs/ef-core/)
before assuming an in-memory match will translate.
