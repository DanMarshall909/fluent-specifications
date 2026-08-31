---
title: Defining rules
description: Build specification catalogs with stable identities, useful failure metadata, pure predicates, and generated language that stays close to the domain.
order: 2
section: Core concepts
---

A good catalog feels less like a framework surface and more like a small domain
vocabulary. Keep the names positive, the predicates pure, and the metadata safe
to show in logs or user-facing explanations.

## Start with a named leaf

Every leaf requires a stable ID, a display name, and an expression over the
candidate type:

```csharp symbol="P:FluentSpecifications.Examples.OrderFulfilment.OrderRules.Paid"
public static Spec<Order> Paid =>
    Spec.Define<Order>("order.paid", "Paid", order => order.Paid);
```

The ID is for diagnostics and telemetry; it is not object equality. The name is
for people and concise rendering. Neither should be derived from expression
text.

Prefer names that describe the positive condition naturally:

| Prefer | Avoid |
| --- | --- |
| `Paid` | `IsPaidSpecification` |
| `HighPriority` | `CheckWhetherPriority` |
| `HasDeliveryAddress` | `OrderSatisfiesAddressRule` |
| `CanShip` | `ShippingSpecification` |

`Is` is not forbidden, but it usually adds noise once the value is already
clearly Boolean.

## Parameterized rules

Arguments belong on catalog methods. Capture the supplied value in the
expression rather than reading mutable ambient state:

```csharp symbol="M:FluentSpecifications.Examples.OrderFulfilment.OrderRules.WorthAtLeast(System.Int32)"
public static Spec<Order> WorthAtLeast(int minimumCents) =>
    Spec.Define<Order>(
        "order.worth-at-least",
        "Worth at least",
        order => order.TotalCents >= minimumCents);
```

Obtain changing values before constructing the rule. For example, pass a
cutoff into `CreatedBefore(cutoff)` instead of reading the current clock inside
the predicate. This makes both in-memory evaluation and provider
parameterization predictable.

## Name important compositions

A named composition preserves its child tree while presenting a useful domain
boundary:

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

Concise rendering stops at `Can ship`; diagnostics and translators can still
walk the paid, address, and suspension rules beneath it.

Failure messages should describe the failed condition without exposing
candidate values. Optional codes and paths are useful for machines; explicit
context is snapshotted and should contain only deliberately non-sensitive data.

## Choose what becomes a domain property

`[Expose]` generates a Boolean extension property for an argument-free rule.
Use it for domain concepts that genuinely improve a call site, such as
`order.CanShip`. Parameterized rules cannot become properties because their
arguments would have nowhere to go.

The generator caches zero-argument rules as stable definitions. Parameterized
rules are constructed per invocation, so argument values are never used as a
global cache key.

## Catalog shapes are intentionally strict

A catalog must be a top-level, non-generic, static partial class. Rules may be
public static get-only properties or public static readonly fields. Mutable
fields, settable properties, generic rule methods, and `ref` or `out`
parameters are compile-time errors rather than members that quietly disappear:

```csharp symbol="M:FluentSpecifications.Generators.Tests.GeneratorDiagnosticTests.Unsupported_rule_shapes_are_reported_instead_of_silently_disappearing"
[Fact]
public void Unsupported_rule_shapes_are_reported_instead_of_silently_disappearing()
{
    const string source = """
        using FluentSpecifications;

        public sealed class Order;

        [SpecificationSet<Order>]
        public static partial class OrderRules
        {
            public static Spec<Order> Mutable =
                Spec.Define<Order>("order.mutable", "Mutable", _ => true);

            public static Spec<Order> Settable { get; set; } =
                Spec.Define<Order>("order.settable", "Settable", _ => true);

            public static Spec<Order> Generic<T>(T value) =>
                Spec.Define<Order>("order.generic", "Generic", _ => value != null);

            public static Spec<Order> WithOutput(out int value)
            {
                value = 1;
                return Spec.Define<Order>("order.output", "Output", _ => true);
            }
        }
        """;

    var result = Run(source);

    var diagnostics = result.Diagnostics
        .Where(item => item.Id == "FSPEC004")
        .ToArray();
    Assert.Equal(4, diagnostics.Length);
    Assert.All(diagnostics, diagnostic =>
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
}
```

The source generator also diagnoses invalid catalogs, exposed names that hide
instance members, and projects that do not use C# 14.

## Keep predicates boring

Specification predicates should be deterministic and free of I/O. Avoid:

- mutable global or closure state;
- clocks read from inside the expression;
- network, filesystem, or database calls;
- logging or mutation as a side effect; and
- domain methods unless every target provider is known to translate them.

Async rules are a separate abstraction. A synchronous Boolean expression is
what makes one rule tree usable for memory, diagnostics, and provider
translation.
