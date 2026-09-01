---
title: Reference and boundaries
description: Review the public concepts, generated syntax, package responsibilities, diagnostics, translation errors, and deliberate non-goals of version one.
order: 8
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

## NuGet packages

The 1.2.0 suite publishes four coordinated packages:

| Package | Contents | Direct package dependencies |
| --- | --- | --- |
| `DanMarshall.FluentSpecifications` | Core runtime and source generator | None |
| `DanMarshall.FluentSpecifications.Repositories` | Provider-neutral `IReadRepository<T>` contract | Starter package |
| `DanMarshall.FluentSpecifications.Expressions` | Parameter-rebound expression translator | Starter package |
| `DanMarshall.FluentSpecifications.EntityFrameworkCore` | Relational executor and repository implementation | Starter, repository, expressions, and EF Core Relational |

The starter package began at 1.0.0 and retains **zero third-party package
dependencies**. The optional extensions join at 1.2.0 and do not become
transitive dependencies of the starter. Installing the EF package brings the
repository and expression assemblies plus Microsoft's relational EF runtime;
install the starter directly as well when its source generator is required.

Maintainers select one coordinated SemVer explicitly in every packable project.
The package-suite script reads each effective `PackageVersion`, rejects any
mismatch, and produces all `.nupkg` and `.snupkg` artifacts together. Existing
package lines accept only the immediate next patch, minor, or major transition;
a new extension may begin at the current suite version.

Publication is an explicitly dispatched GitHub Actions workflow whose requested
version must match the projects. Before requesting a credential, it compares
every package ID with NuGet.org. Idempotent retries allow recovery from a
partially completed suite publication, while NuGet's immutable package versions
still prevent an artifact from being replaced.

Releases use NuGet.org Trusted Publishing. The publisher requests a short-lived
OIDC credential immediately before pushing the packages, so no long-lived NuGet
API key is stored in the repository or its CI settings.

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

The repository targets C# 14 and .NET 10. The coordinated 1.2.0 suite keeps the
starter dependency-free while making the repository contract, expression
translator, and EF Core implementation independently installable. The
checked-in specification and executable tests define its behavioral contract.
