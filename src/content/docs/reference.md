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

## Project responsibilities

| Project | Owns | Must not own |
| --- | --- | --- |
| `FluentSpecifications.Core` | Rule tree, evaluation, diagnostics, traversal contracts | EF or query-provider types |
| `FluentSpecifications.Generators` | Catalog discovery and C# 14 extension members | Runtime query execution |
| `FluentSpecifications.Expressions` | Parameter-rebound expression plans | `IQueryable` application |
| `FluentSpecifications.EntityFrameworkCore` | Relational preflight and materialization | Domain repository contracts |
| `FluentSpecifications.Docs` | Roslyn symbol extraction and Markdown synchronization | Business-rule execution |

Dependencies point inward. Core has no reference to a provider adapter.

## NuGet package

The 1.x line is packaged as `DanMarshall.FluentSpecifications`, beginning at
1.0.0. Each push to `main` receives the next patch version. The package combines
the core runtime with the source generator so the normal install is one package
reference. Its NuGet dependency list is empty: there are **zero third-party
package dependencies**, and no Microsoft runtime or compiler DLLs are bundled.
The package uses only the .NET and Roslyn platform supplied by Microsoft.

The expression and EF Core projects remain separate infrastructure adapters;
they are not transitive dependencies of the starter package.

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

The repository targets C# 14 and .NET 10. `DanMarshall.FluentSpecifications`
1.0.0 is the version-one starter package; the checked-in specification and
executable tests define its behavioral contract.
