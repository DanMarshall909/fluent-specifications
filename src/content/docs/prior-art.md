---
title: Prior art and design lineage
description: See the libraries and policy systems that shaped Fluent Specifications, including the internal Kotlin API that established its terse syntax.
order: 9
section: Reference
---

Fluent Specifications is an original implementation, but not an isolated idea.
It draws on a long line of specification, rule, validation, and policy systems.
These projects are **influences, not compatibility targets**.

## The closest ergonomic influence

The strongest call-site influence is an internal Kotlin specification library
used at Reapit. Its fluent, discoverable chains showed that composed rules could
read naturally without making every operation look like a function call. That
implementation is not public, so there is no repository to link; this library
reinterprets the experience for C# rather than reproducing its source or API.

That lineage is most visible in three choices:

- zero-argument rules compose without parentheses;
- parentheses remain available for grouping and parameterized rules; and
- named domain rules are preferred over clever Boolean operator overloads.

## Specification libraries

- [Ardalis.Specification](https://github.com/ardalis/Specification) demonstrates
  a mature C# specification ecosystem and a disciplined repository boundary.
  Fluent Specifications narrows its core to Boolean rules and deliberately does
  not make query shaping part of the domain object.
- [Spring Data JPA Specifications](https://docs.spring.io/spring-data/jpa/reference/jpa/specifications.html)
  demonstrates composable predicates backed by a persistence provider. It is a
  useful precedent for algebraic composition, while this library keeps provider
  APIs behind infrastructure.
- [NSpecifications](https://github.com/miholler/NSpecifications) explores terse,
  readable C# specification composition. Its ergonomics informed the search for
  a compact call site without requiring Boolean operator overloads.
- [RulerZ](https://github.com/K-Phoen/rulerz) separates a rule from the mechanism
  that evaluates it against different targets. That separation influenced the
  closed rule tree and provider-facing traversal contracts here.
- [spec-pattern for TypeScript](https://github.com/thiagodp/spec-pattern) and
  [Happyr Doctrine Specification](https://github.com/Happyr/Doctrine-Specification)
  show how the same pattern changes when adapted to different language and data
  access conventions.

## DSL and policy influences

- [Konform](https://github.com/konform-kt/konform) is a Kotlin validation DSL
  whose readable nesting and language-native feel reinforced the value of
  discoverable fluent syntax.
- [Cedar](https://github.com/cedar-policy/cedar) and
  [Open Policy Agent](https://github.com/open-policy-agent/opa) demonstrate the
  value of explicit policy structure, predictable evaluation, and useful
  diagnostics. Fluent Specifications borrows those qualities, not their policy
  languages or authorization models.

## Where this implementation draws its boundary

The combination is intentional: a small immutable Boolean tree, generated C#
syntax, structured diagnostics, and provider translation that never exposes
`IQueryable` through the domain boundary. For the normative decisions, see the
[repository guide](/docs/repositories/), [reference](/docs/reference/), and
[EF Core guide](/docs/ef-core/).
