---
title: Composition
description: Combine specifications with readable connector properties, explicit grouping, word-based negation, parameterized rules, constants, and aggregate laws.
order: 3
section: Core concepts
---

Composition always returns another `Spec<T>`. It never collapses into a
delegate or expression that has forgotten its named children.

## Zero-argument rules stay terse

Generated connector properties remove parentheses when the right-hand rule is
already named by the catalog:

```csharp symbol="M:FluentSpecifications.Generator.IntegrationTests.GeneratedDslTests.Zero_argument_rules_compose_without_parentheses"
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
```

The connectors use words rather than overloaded `&`, `|`, or `!` operators:

| Syntax | Meaning |
| --- | --- |
| `a.And.b` | both rules must pass |
| `a.Or.b` | either rule may pass |
| `a.AndNot.b` | `a` passes and `b` does not |
| `a.OrNot.b` | `a` passes or `b` does not |
| `a.Not` | invert `a` |

## Parentheses mean something

Invoke a connector when the right side is grouped or computed dynamically:

```csharp symbol="M:FluentSpecifications.Generator.IntegrationTests.GeneratedDslTests.Connector_invocation_supports_explicit_grouping"
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
```

This makes `CanShip.And(HighPriority.Or.ManualOverride)` visibly different from
a flat chain. Rendering preserves the same grouping.

## Arguments keep their parentheses

Parameterized rules remain methods, but the connective itself stays terse:

```csharp symbol="M:FluentSpecifications.Generator.IntegrationTests.GeneratedDslTests.Parameterized_rules_keep_parentheses_only_for_arguments"
[Fact]
public void Parameterized_rules_keep_parentheses_only_for_arguments()
{
    var rule = CanShip.And.WorthAtLeast(100m);

    Assert.True(rule.Matches(new Order(Paid: true, HasAddress: true, Total: 150m)));
    Assert.False(rule.Matches(new Order(Paid: true, HasAddress: true, Total: 50m)));
}
```

The generated surface preserves overloads, `params`, optional arguments, and
escaped C# keywords:

```csharp symbol="M:FluentSpecifications.Generator.IntegrationTests.GeneratedDslTests.Params_overloads_and_keyword_parameter_names_survive_generation"
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
```

These awkward signatures are covered because source-generator bugs tend to
hide in precisely this kind of ordinary C# detail.

## Negation uses words

Use `Suspended.Not` for a standalone negation, or `AndNot` and `OrNot` inside a
chain. Double negation preserves behavior. A failed unnamed negation uses a
neutral diagnostic message rather than reusing the positive rule's failure
text as though it described the opposite condition.

## Constants and empty aggregates

`Always<T>()` and `Never<T>()` make the absence of a restriction explicit.
`AllOf` and `AnyOf` take immutable snapshots of their input and enumerate it
exactly once.

The empty cases follow Boolean identity laws:

```csharp symbol="M:FluentSpecifications.Core.Tests.FactoryAndAggregateTests.Empty_aggregates_follow_boolean_identity_laws"
[Fact]
public void Empty_aggregates_follow_boolean_identity_laws()
{
    Assert.True(Spec.AllOf(Array.Empty<Spec<Order>>()).Matches(new Order()));
    Assert.False(Spec.AnyOf(Array.Empty<Spec<Order>>()).Matches(new Order()));
}
```

An empty disjunction does not mean “return everything.” Use `Always<T>()` when
the caller intentionally supplies no restriction.

## Evaluation order

`Matches` evaluates left to right and short-circuits just like normal Boolean
code:

```csharp symbol="M:FluentSpecifications.Core.Tests.CompositionTests.Matches_short_circuits_and_from_left_to_right"
[Fact]
public void Matches_short_circuits_and_from_left_to_right()
{
    var probe = new EvaluationProbe();
    var left = Rule("left", _ => false);
    var right = Rule("right", _ => probe.Return(true));

    Assert.False(left.And(right).Matches(new Order()));
    Assert.False(probe.WasEvaluated);
}
```

That behavior is suitable for a fast yes-or-no decision. Diagnostic evaluation
has a different default because an explanation often needs to inspect every
relevant branch.
