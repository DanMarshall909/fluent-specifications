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
| `Order.Search` | Generated, provider-neutral search entry point |
| `Order.Rules` | Explicit generated rule catalog for dynamic composition |
| `Order.Fields` | Strongly typed generated field catalog |
| `Search<T>` | Immutable filter, ordering, and optional paging description |
| `Page<T>` | Materialized results plus page and total metadata |
| `IReadRepository<T>` | Optional provider-neutral materializing read contract |

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
| inferred search rule | `Order.Search.Matching.CanShip` |
| primary field ordering | `.Sorted.By.CreatedAt.Desc` |
| tie-break ordering | `.Then.By.Id.Asc` |
| one-based page | `.Page(2).OfSize(50)` |

The first version deliberately does not overload Boolean operators and does not
implicitly convert between specifications, expressions, delegates, or Boolean
values.

## Project responsibilities

| Project | Owns | Must not own |
| --- | --- | --- |
| `FluentSpecifications.Core` | Rule tree, immutable searches, generated-field descriptors, pages, evaluation, diagnostics, traversal contracts | EF or query-provider types |
| `FluentSpecifications.Generators` | Catalog discovery and C# 14 rule, field, and search extension members | Runtime query execution |
| `FluentSpecifications.Expressions` | Parameter-rebound expression plans | `IQueryable` application |
| `FluentSpecifications.Repositories` | Provider-neutral materializing read contract | Provider or deferred-query types |
| `FluentSpecifications.EntityFrameworkCore` | Relational preflight and `IReadRepository<T>` implementation | Application repository policy |
| `FluentSpecifications.Docs` | Roslyn symbol extraction and Markdown synchronization | Business-rule execution |

Dependencies point inward. Core has no reference to the repository contract or
a provider adapter. The repository contract depends only on Core; EF Core
depends on that contract and supplies one implementation.

## NuGet package

The 1.x line is packaged as `DanMarshall.FluentSpecifications`, beginning at
1.0.0. Maintainers select the next SemVer explicitly with the Core project's
`Version`; CI reads its exact effective `PackageVersion` instead of deriving one
from Git history. The package combines the core runtime with the source
generator so the normal install is one package reference. Its NuGet dependency
list is empty: there are **zero third-party package dependencies**, and no
Microsoft runtime or compiler DLLs are bundled. The package uses only the .NET
and Roslyn platform supplied by Microsoft.

Before packing or requesting a publishing credential, CI compares the selected
version with NuGet.org. Only the immediate next patch, minor, or major SemVer
transition is accepted; unchanged versions, duplicates, gaps, and
prerelease-shaped versions fail the release.

The repository contract, expression adapter, and EF Core implementation remain
separate extensions; they are not transitive dependencies of the starter
package.

Releases use NuGet.org Trusted Publishing through GitHub Actions. The publisher
requests a short-lived OIDC credential immediately before pushing the package,
so no long-lived NuGet API key is stored in the repository or its CI settings.

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
| `FSPEC005` | More than one catalog tries to generate inferred search language for an entity |
| `FSPEC006` | An entity member would hide `Search`, `Rules`, or `Fields` |
| `FSPEC007` | A catalog member would collide with a generated search-support type |

Search generation is opt-in for 1.x source compatibility. Mark the one catalog
that owns an entity's inferred search language with `GenerateSearch = true` on
its `SpecificationSet<T>` attribute. Other catalogs keep generating their
normal specification connectors. Generated fields include effective inherited
public readable members, while indexers, static members, and inaccessible
getters are omitted. Existing catalog members named `SearchRoot`, `RuleCatalog`,
`SearchRuleCatalog`, or `FieldCatalog` are diagnosed because those names are
reserved for generated support types. If a field name collides with an
inherited `object` member on the selector, use the explicit dynamic form
`.Sorted.By[Order.Fields.ToString]` or `.Then.By[Order.Fields.ToString]`.

## Deliberate non-goals

A specification is not a container for:

- sorting, paging, projection, joins, or includes;
- tracking, split-query, caching, or provider flags;
- mutations, commands, or async workflows;
- arbitrary validation UI state;
- a promise that every .NET expression translates everywhere; or
- a CRUD repository framework.

Those exclusions keep `a.Or.b` meaningful. Sorting and paging therefore live
in the separate immutable `Search<T>` description. Searches still exclude
projection, joins, includes, tracking, split-query settings, and provider
objects. The optional `IReadRepository<T>` contract standardizes only the small
materializing read surface already represented by specifications and searches.

## Current status

The repository targets C# 14 and .NET 10. `DanMarshall.FluentSpecifications`
1.1.0 adds opt-in, provider-neutral search shaping to the 1.0.0 starter
package; the checked-in specification and executable tests define its
behavioral contract.
