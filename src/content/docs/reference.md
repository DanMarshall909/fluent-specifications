---
title: Reference and boundaries
description: Review the public concepts, generated syntax, package responsibilities, diagnostics, translation errors, and deliberate non-goals of version one.
order: 7
section: Reference
---

Fluent Specifications keeps a narrow center: an immutable, named Boolean tree
over one candidate type. The surrounding packages add syntax, diagnostics, and
infrastructure translation without changing that meaning.

## Core concepts

| Concept | Responsibility |
| --- | --- |
| `Spec<T>` | Immutable Boolean rule and composition surface |
| `Spec.Define` | Create a named leaf from an expression |
| `Spec.Always<T>()` | Explicit rule that always passes |
| `Spec.Never<T>()` | Explicit rule that never passes |
| `Spec.AllOf` | Conjunction over a single immutable snapshot |
| `Spec.AnyOf` | Disjunction over a single immutable snapshot |
| `Matches` | Fast, short-circuiting in-memory Boolean evaluation |
| `Check` | Structured complete or short-circuit diagnostics |
| `Named` | Add a domain boundary without discarding child rules |
| `Accept` | Provider-facing traversal over the closed rule tree |

The retained node kinds are `Always`, `Never`, `Leaf`, `Named`, `And`, `Or`, and
`Not`. `AndNot`, `OrNot`, `AllOf`, and `AnyOf` are construction conveniences
that normalize to those nodes.

## Generated language

| Catalog member | Generated connector form |
| --- | --- |
| `HighPriority` | `rule.And.HighPriority` |
| `Suspended` | `rule.AndNot.Suspended` |
| `WorthAtLeast(int)` | `rule.And.WorthAtLeast(10_000)` |
| grouped or dynamic rule | `rule.And(otherRule)` |
| `[Expose] CanShip` | `order.CanShip` |

The first version deliberately does not overload Boolean operators and does not
implicitly convert between specifications, expressions, delegates, or Boolean
values.

## Package responsibilities

| Package | Owns | Must not own |
| --- | --- | --- |
| `FluentSpecifications.Core` | Rule tree, evaluation, diagnostics, traversal contracts | EF or query-provider types |
| `FluentSpecifications.Generators` | Catalog discovery and C# 14 extension members | Runtime query execution |
| `FluentSpecifications.Expressions` | Parameter-rebound expression plans | `IQueryable` application |
| `FluentSpecifications.EntityFrameworkCore` | Relational preflight and materialization | Domain repository contracts |
| `FluentSpecifications.Docs` | Roslyn symbol extraction and Markdown synchronization | Business-rule execution |

Dependencies point inward. Core has no reference to a provider adapter.

## Failure types

| Type | Meaning |
| --- | --- |
| `RuleFailure` | A predicate returned `false` as a normal business result |
| `EvaluationError` | A predicate threw during diagnostic evaluation |
| `SpecificationEvaluationException` | Fast Boolean evaluation could not produce a result |
| `TranslationError` | Infrastructure could not prepare a provider plan |
| `SpecificationTranslationException` | A failed plan was requested for execution |

Translation errors carry a stable code, node path, and rule ID when a specific
leaf is responsible. They do not contain candidate values.

## Generator diagnostics

| Code | Meaning |
| --- | --- |
| `FSPEC001` | The specification catalog shape is invalid |
| `FSPEC002` | An exposed property would hide an instance member |
| `FSPEC003` | Generated extension properties require C# 14 |
| `FSPEC004` | A rule member shape cannot be represented safely |

## Deliberate non-goals

A specification is not a container for:

- sorting, paging, projection, joins, or includes;
- tracking, split-query, caching, or provider flags;
- mutations, commands, or async workflows;
- arbitrary validation UI state;
- a promise that every .NET expression translates everywhere; or
- a universal repository framework.

Those exclusions keep `a.Or.b` meaningful. A query modifier cannot generally be
combined with Boolean algebra without surprising behavior.

## Current status

The repository is an implemented draft targeting C# 14 and .NET 10. Packaging
and NuGet publication are still pending. Treat the checked-in specification and
executable tests as the version-one contract while the package surface is being
prepared.
