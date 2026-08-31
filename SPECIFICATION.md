# Fluent Specifications

## Library specification

Status: Implemented through 1.1 search shaping
Date: 2026-08-31

This document defines the required behaviour and intended public experience of
the Fluent Specifications library. Examples are normative unless marked as
illustrative.

The key words **MUST**, **MUST NOT**, **SHOULD**, **SHOULD NOT**, and **MAY** are
to be interpreted as requirements on the implementation.

## 1. Purpose

Fluent Specifications provides named, reusable business rules that can be:

- read naturally in application code;
- evaluated against an object;
- composed with Boolean logic;
- explained with useful failure information; and
- handed to a repository without exposing a database query API.

The normal experience should look like this:

```csharp
using static OrderRules;

var ready = CanShip.And.HighPriority.AndNot.Suspended;
var exceptional = CanShip.And(HighPriority.Or.ManualOverride);
var valuable = CanShip.And.WorthAtLeast(100m);

if (order.CanShip)
{
    await dispatcher.DispatchAsync(order, cancellationToken);
}

var orders = await orderRepository.ListAsync(ready, cancellationToken);
```

The syntax is deliberately based on domain language. Application code should
not need to say `IsSatisfiedBy`, `Satisfies`, `Evaluate`, `Invoke`, or
`Expression` for routine use.

## 2. Design principles

### 2.1 Business rules, not query objects

A `Spec<T>` represents only a Boolean rule over `T`.

It MUST NOT contain:

- ordering;
- paging;
- projection;
- eager-loading instructions;
- joins;
- tracking or split-query flags;
- caching directives; or
- provider-specific query configuration.

Those concerns are not closed under Boolean composition. Mixing them into a
specification makes expressions such as `a.Or.b` ambiguous or incorrect.

Sorting and paging belong to a separate immutable `Search<T>` description.
`Search<T>` MAY contain one `Spec<T>`, but a `Spec<T>` MUST never contain or
inherit search shaping.

### 2.2 No persistence API in application code

The core and generator packages MUST NOT expose `IQueryable`, `IQueryProvider`,
Entity Framework types, or another persistence provider's types in their public
API.

Application code passes a specification to a repository. Infrastructure may
translate and apply it internally.

### 2.3 Composition must remain a specification

Every operation that composes a `Spec<T>` MUST return another `Spec<T>`.
Composition MUST NOT silently fall back to an opaque predicate, delegate, or a
less capable interface.

### 2.4 Named rules are first-class

A rule has domain identity beyond its implementation expression. Names and
stable IDs MUST survive composition so that rules can be rendered, diagnosed,
translated, tested, and observed without reverse-engineering expression text.

### 2.5 Errors are not failed rules

A predicate returning `false` is a normal business-rule failure. A predicate
throwing, or a provider being unable to translate a rule, is an error. The
library MUST NOT silently convert either kind of error to `false`.

### 2.6 Immutability

`Spec<T>` and every library-owned node MUST be immutable and thread-safe.
User-supplied predicates and captured values are expected to be pure and
thread-safe.

## 3. Language and platform

The full fluent syntax targets C# 14 or later because it uses generated
extension properties.

The core composition mechanism MUST also allow explicit grouping by invoking a
connector:

```csharp
var rule = CanShip.And(HighPriority.Or.ManualOverride);
```

This form remains useful when the right-hand side is computed dynamically or
when generated extension properties are unavailable.

The initial implementation SHOULD target currently supported .NET versions.
Exact target frameworks are a packaging decision and do not alter the semantics
defined here.

## 4. Terminology

- **Rule**: a Boolean business condition represented by `Spec<T>`.
- **Leaf**: a rule defined directly by a typed predicate.
- **Named rule**: a leaf or composed rule with a stable ID, display name, and
  optional failure metadata.
- **Connector**: the value returned by `.And`, `.Or`, `.AndNot`, or `.OrNot`.
- **Catalog**: a static partial class containing the named rules for a domain
  type.
- **Candidate**: the object against which a rule is evaluated.
- **Preparation**: translation of a specification into a provider-specific,
  reusable execution plan.
- **Search**: an immutable, provider-neutral description combining a Boolean
  rule with optional ordering and paging.
- **Field**: a generated, strongly typed selector for a readable entity member.

## 5. Defining rules

Rules SHOULD be grouped in a catalog:

```csharp
[SpecificationSet<Order>]
public static partial class OrderRules
{
    [Expose]
    public static Spec<Order> Paid =>
        Spec.Define<Order>(
            id: "order.paid",
            name: "Paid",
            predicate: order => order.PaymentStatus == PaymentStatus.Paid,
            failure: "Payment has not been received.");

    [Expose]
    public static Spec<Order> HasDeliveryAddress =>
        Spec.Define<Order>(
            id: "order.has-delivery-address",
            name: "Has delivery address",
            predicate: order => order.DeliveryAddress != null,
            failure: "A delivery address is required.",
            path: "DeliveryAddress");

    public static Spec<Order> Cancelled =>
        Spec.Define<Order>(
            id: "order.cancelled",
            name: "Cancelled",
            predicate: order => order.Status == OrderStatus.Cancelled,
            failure: "The order is not cancelled.");

    public static Spec<Order> WorthAtLeast(decimal amount) =>
        Spec.Define<Order>(
            id: "order.worth-at-least",
            name: "Worth at least",
            predicate: order => order.Total >= amount,
            failure: "The order total is below the required amount.");

    [Expose]
    public static Spec<Order> CanShip =>
        Paid
            .And(HasDeliveryAddress)
            .AndNot(Cancelled)
            .Named(
                id: "order.can-ship",
                name: "Can ship",
                failure: "The order is not ready to ship.");
}
```

A catalog MUST be a top-level, non-generic, static partial class. The candidate
type is supplied by `SpecificationSet<T>`. These constraints allow the generator
to place C# extension blocks in the catalog itself.

Rules MAY be public static get-only properties or public static readonly
fields. Mutable fields, settable rule properties, generic rule methods, and
`ref`/`out` rule parameters MUST produce a compile-time diagnostic rather than
being silently omitted from the fluent surface.

`Spec.Define` MUST require:

- a stable, non-empty rule ID;
- a non-empty display name; and
- an `Expression<Func<T, bool>>` predicate.

It MUST allow optional failure metadata including a safe human message, a
machine-readable code, a domain path, and non-sensitive context.

`Named` MUST wrap a rule without discarding its child tree. It establishes a
domain boundary for default rendering and diagnostics while retaining the
underlying structure for detailed explanations and translation.

Rule names SHOULD describe the positive condition and SHOULD omit `Is` when the
result remains natural:

```csharp
Paid
HighPriority
Suspended
CanShip
HasDeliveryAddress
```

The following names are discouraged:

```csharp
IsPaidSpecification
OrderSatisfiesShippingRules
CheckWhetherOrderCanShip
```

## 6. Generated fluent API

For every accessible zero-argument rule in a specification catalog, the source
generator MUST make that rule available as an extension property on the
catalog's connector type.

For every accessible parameterized rule, the generator MUST make it available
as an extension method on the connector type.

Generated extension blocks MUST be emitted into the catalog's partial static
class. Consequently, the same `using static` directive that imports its rule
names also makes its extension members available for extension lookup.

The generated shape is conceptually:

```csharp
public static partial class OrderRules
{
    extension(SpecConnector<Order> connector)
    {
        public Spec<Order> HighPriority =>
            connector(OrderRules.HighPriority);

        public Spec<Order> WorthAtLeast(decimal amount) =>
            connector(OrderRules.WorthAtLeast(amount));
    }

    extension(Order order)
    {
        public bool CanShip => OrderRules.CanShip.Matches(order);
    }
}
```

The actual generated implementation MAY be organized differently, but it MUST
have the same lookup and evaluation behaviour.

Given the catalog above, the generated surface enables:

```csharp
Paid.And.HasDeliveryAddress
Paid.AndNot.Cancelled
Paid.Or.ManualOverride
Paid.And.WorthAtLeast(100m)
```

The generated member MUST compose the left-hand specification with the selected
catalog rule. It MUST NOT evaluate either rule during composition.

The generator MUST cache each zero-argument catalog rule once using thread-safe
lazy initialization. This makes expression-bodied catalog properties stable and
prevents rebuilding and recompiling their expression trees on every fluent or
domain-property access. Parameterized rules are constructed per invocation and
MUST NOT be globally cached by argument value.

### 6.1 Domain properties

`[Expose]` on a zero-argument rule MUST generate a Boolean extension property on
the candidate type:

```csharp
if (order.CanShip)
{
    // ...
}
```

The generated property is equivalent to:

```csharp
OrderRules.CanShip.Matches(order)
```

Exposure is opt-in. Parameterized rules cannot be exposed as properties because
they require arguments.

The generator MUST report a compile-time diagnostic when an exposed name:

- conflicts with an accessible instance member;
- is ambiguous with another generated extension member; or
- cannot be emitted safely for the candidate type.

It MUST NOT silently generate a property that resolves to a different member at
the call site.

### 6.2 Catalog ambiguity

Rules in the same catalog MUST have unique generated member signatures. When
multiple catalogs for the same candidate type are imported and produce an
ambiguous member, normal C# qualification MUST remain available:

```csharp
var rule = FulfilmentOrderRules.CanShip
    .And(RiskOrderRules.NotUnderReview);
```

The generator SHOULD diagnose ambiguities it can see in the current
compilation.

Exactly one catalog per candidate type MAY opt in to the inferred
`Order.Search`, `Order.Rules`, and `Order.Fields` language by setting
`GenerateSearch = true` on `SpecificationSet<T>`. Search generation MUST remain
off when the option is omitted so existing specification catalogs retain their
1.x source shape. Multiple search-generating catalogs MUST produce a
compile-time diagnostic rather than leaving ambiguous entity entry points.
Other catalogs continue to generate their ordinary `Spec<T>` connector
language.

## 7. Core API

The conceptual public surface is:

```csharp
public sealed class Spec<T>
{
    public SpecConnector<T> And { get; }
    public SpecConnector<T> Or { get; }
    public SpecConnector<T> AndNot { get; }
    public SpecConnector<T> OrNot { get; }
    public Spec<T> Not { get; }

    public bool Matches(T candidate);
    public CheckResult Check(T candidate, CheckOptions? options = null);
    public TResult Accept<TResult>(ISpecVisitor<T, TResult> visitor);

    public Spec<T> Named(
        string id,
        string name,
        string? failure = null,
        string? code = null,
        string? path = null,
        IReadOnlyDictionary<string, object?>? context = null);
}

public delegate Spec<T> SpecConnector<T>(Spec<T> right);

public static class Spec
{
    public static Spec<T> Define<T>(
        string id,
        string name,
        Expression<Func<T, bool>> predicate,
        string? failure = null,
        string? code = null,
        string? path = null,
        IReadOnlyDictionary<string, object?>? context = null);

    public static Spec<T> Always<T>();
    public static Spec<T> Never<T>();
    public static Spec<T> AllOf<T>(IEnumerable<Spec<T>> specifications);
    public static Spec<T> AnyOf<T>(IEnumerable<Spec<T>> specifications);
}
```

This declaration describes the required shape rather than committing to every
overload or metadata representation.

`Spec<T>` MUST be sealed. The library MUST NOT require consumers to implement a
public specification interface. A single closed representation avoids losing
composition, diagnostics, or translation capabilities according to a value's
static type.

`Spec<T>` MUST be invariant in `T`. Cross-type adaptation is outside the first
version.

The library MUST NOT provide implicit conversions between specifications,
predicates, delegates, expressions, or Boolean values. Such conversions make
overload resolution and provider behaviour difficult to predict.

The first version MUST NOT overload `&`, `|`, `!`, `true`, or `false`.

## 8. Composition syntax

### 8.1 Named right-hand rule

Generated connector properties are preferred when the right-hand rule has a
catalog name:

```csharp
CanShip.And.HighPriority
CanShip.AndNot.Suspended
CanShip.Or.ManualOverride
CanShip.OrNot.International
```

### 8.2 Parameterized rule

Arguments require parentheses, but the connective does not:

```csharp
CanShip.And.WorthAtLeast(100m)
CanShip.And.CreatedAfter(cutoff)
```

### 8.3 Grouped or dynamic rule

The connector is invocable when an arbitrary specification is needed:

```csharp
CanShip.And(HighPriority.Or.ManualOverride)
CanShip.And(BuildRegionalRule(region))
```

Parentheses are required where they express grouping or pass a computed value.
They SHOULD NOT be required merely to name a zero-argument rule.

### 8.4 Negation

Negation is expressed with words:

```csharp
Suspended.Not
CanShip.AndNot.Suspended
CanShip.And(Suspended.Or.Fraudulent).Not
```

`a.AndNot.b` is exactly `a.And(b.Not)`. `a.OrNot.b` is exactly
`a.Or(b.Not)`.

## 9. Rule tree

The library MUST retain a provider-neutral, library-owned tree containing these
logical node kinds:

```text
Always
Never
Leaf
Named
And
Or
Not
```

`AndNot`, `OrNot`, `AllOf`, and `AnyOf` are construction conveniences and MUST
normalize to the node kinds above.

The tree MUST retain:

- the typed predicate at each leaf;
- rule identity and diagnostic metadata;
- explicit logical grouping; and
- the path to every node.

The implementation MAY flatten associative `And` and `Or` nodes and MAY remove
identity constants, provided that observable evaluation, diagnostic grouping,
named boundaries, and rendering remain correct.

It MUST NOT eagerly flatten the entire rule into a single combined expression,
because doing so would discard high-level identity and explanation structure.

Provider adapters in separate assemblies MUST receive a supported, read-only
traversal API. Core SHOULD expose this as a visitor accepted by `Spec<T>` rather
than exposing public node constructors. The visitor MUST distinguish every node
kind, receive leaf expressions and rule metadata, and preserve child order.
Application code is not expected to use this lower-level API.

## 10. Boolean semantics

For ordinary pass/fail outcomes, composition follows Boolean algebra:

| Expression | Result |
| --- | --- |
| `a.And.b` | passes only when both pass |
| `a.Or.b` | passes when either passes |
| `a.Not` | passes exactly when `a` fails |
| `a.AndNot.b` | passes when `a` passes and `b` fails |
| `a.OrNot.b` | passes when `a` passes or `b` fails |

The constants and empty aggregates MUST have these meanings:

```text
Always<T>       passes
Never<T>        fails
AllOf([])       Always<T>
AnyOf([])       Never<T>
```

Consequently:

```text
a AND Always = a
a OR Never   = a
a AND Never  = Never
a OR Always  = Always
NOT NOT a    = a
```

These are semantic identities. They do not imply reference, object, or
structural equality.

An empty disjunction MUST NOT mean “no filtering” or match every candidate.
Callers that need no restriction must say `Always<T>()` explicitly.

`AllOf` and `AnyOf` MUST enumerate their input exactly once, reject null
elements, and produce immutable snapshots.

`Always` and `Never` MUST have stable library-owned identities and safe default
rendering. A diagnostic failure from an unwrapped `Never` uses a neutral
library-owned message; callers needing domain wording SHOULD wrap it with
`Named`.

## 11. Evaluation

### 11.1 Fast Boolean evaluation

`Matches` MUST:

- evaluate in memory;
- evaluate left to right;
- short-circuit `And` and `Or`;
- return only normal pass/fail outcomes; and
- throw `SpecificationEvaluationException` when evaluation cannot produce a
  Boolean result.

The exception MUST identify the failing rule ID and node path and MUST preserve
the original exception as its inner exception. It MUST NOT include candidate
values in its message by default.

Generated domain properties use this same behaviour.

### 11.2 Diagnostic evaluation

`Check` MUST return a structured `CheckResult` containing:

- an outcome: `Passed`, `Failed`, or `Error`;
- zero or more rule failures;
- zero or more evaluation errors; and
- optional evaluation trace data when explicitly requested.

Each failure SHOULD contain:

- stable rule ID;
- display name;
- safe message;
- optional machine-readable code;
- optional domain path;
- node path within the composed rule; and
- explicitly supplied, non-sensitive context.

Each error MUST contain the applicable rule ID and node path. An in-memory error
MAY retain the exception object for programmatic inspection, but default text
rendering MUST redact exception data that may contain candidate values.

### 11.3 Complete diagnostics

Leaf rules are required to be pure. The default `Complete` diagnostic mode MUST
therefore evaluate every leaf in the tree, from left to right, even after the
Boolean outcome is known. Outcome combination follows these determining rules:

- for `And`, any failure determines `Failed`; otherwise any error determines
  `Error`; otherwise the result is `Passed`;
- for `Or`, any pass determines `Passed`; otherwise any error determines
  `Error`; otherwise the result is `Failed`; and
- for `Not`, `Passed` and `Failed` are inverted while `Error` remains `Error`.

Errors found in non-determining branches MUST still be attached to the result.
For example, an `Or` with one passing branch and one erroneous branch has a
`Passed` outcome and also reports the error. The error is visible but does not
change an outcome already determined by Boolean logic.

Top-level failures MUST include only failures that explain a final `Failed`
outcome. Failed alternatives beneath a passing `Or`, and failures beneath a
passing `Not`, MAY appear in a requested trace but MUST NOT appear as top-level
business failures. Evaluation errors are always attached, including errors from
non-determining branches in `Complete` mode.

`CheckOptions` MUST offer a `ShortCircuit` mode for lower-cost diagnostics. The
mode and whether evaluation was complete MUST be recorded in `CheckResult`.

### 11.4 Diagnostic grouping

When an `And` fails, failures from all evaluated failing children MAY be
presented together.

When an `Or` fails, alternatives MUST remain grouped. The renderer MUST NOT
flatten their leaf messages into a list that incorrectly suggests every
alternative was independently mandatory.

When `Not` fails, it MUST NOT reuse the positive child's failure message as if
that message described the negated condition. A named negated group uses its own
failure message. An unnamed negation uses a neutral message such as “Expected
the rule not to match.”

## 12. Candidate nullability

C# nullable reference annotations are not distinct runtime generic types. The
library therefore MUST NOT pretend it can reliably distinguish `Spec<Order>`
from `Spec<Order?>` at runtime.

The candidate, including null, is passed to the rule tree. Normal nullable
analysis warns when application code passes null to `Spec<Order>`. For an
explicitly nullable candidate type such as `Spec<Order?>`, the predicate
determines the result:

```csharp
var missing = Spec.Define<Order?>(
    "order.missing",
    "Missing",
    order => order == null);

missing.Matches(null); // true
```

If a predicate dereferences a null candidate and throws, normal evaluation-error
semantics apply. The exception MUST NOT be converted to a business failure.

Provider translation MUST preserve the same intended null semantics or return a
translation error. It MUST NOT silently rely on different provider null
behaviour.

## 13. Captured values and time

Parameterized rules may capture their arguments:

```csharp
public static Spec<Order> CreatedBefore(DateTimeOffset cutoff) =>
    Spec.Define<Order>(
        id: "order.created-before",
        name: "Created before",
        predicate: order => order.CreatedAt < cutoff);
```

Captured values SHOULD be immutable. A rule MUST NOT depend on mutable global
state, ambient tenant state, or a changing clock.

Time-dependent values MUST be obtained before constructing the rule and passed
as explicit parameters. For example, use `ExpiredAt(clock.UtcNow)` rather than
reading `DateTimeOffset.UtcNow` inside the predicate.

The analyzer SHOULD warn about mutable closure captures and common ambient-time
access. A translator MAY reject captures it cannot safely parameterize.

## 14. Repository boundary

The library does not require a universal repository interface. A consuming
application's repository SHOULD accept specifications directly:

```csharp
public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> ListAsync(
        Spec<Order> specification,
        CancellationToken cancellationToken);

    Task<bool> AnyAsync(
        Spec<Order> specification,
        CancellationToken cancellationToken);
}
```

Application usage remains persistence-neutral:

```csharp
var orders = await orderRepository.ListAsync(
    OrderRules.CanShip.And.HighPriority,
    cancellationToken);
```

The specification does not encode whether the operation is `List`, `Any`,
`Count`, `First`, or another repository action. The repository method owns that
choice.

### 14.1 Provider-neutral search shaping

Applications MAY use the generated search language when a repository operation
needs filtering, ordering, and paging together:

```csharp
var request = Order.Search
    .Matching.CanShip.And.HighPriority
    .Sorted.By.CreatedAt.Desc
    .Then.By.Id.Asc
    .Page(2).OfSize(50);

var page = await orderRepository.FindAsync(request, cancellationToken);
```

The example above is normative. The generated fluent surface MUST provide:

- `Order.Search`, where `Order` is the candidate entity type;
- `.Matching.<Rule>` and `.Matching.<ParameterizedRule>(...)`, inferred from
  the entity's specification catalog;
- `.For(specification)` on `Order.Search` for a dynamically composed rule;
- `Order.Rules.<Rule>` as the explicit generated rule catalog;
- `Order.Fields.<Field>` as the explicit generated field catalog;
- `.Sorted.By.<Field>.Asc|Desc` for primary ordering;
- `.Then.By.<Field>.Asc|Desc` for every subsequent ordering;
- `.Page(number).OfSize(size)` for one-based paging; and
- `.All` as the explicit unfiltered starting point.

The generated shorthand MUST be equivalent to the explicit form:

```csharp
var rule = Order.Rules.CanShip.And.HighPriority;

var request = Order.Search
    .For(rule)
    .Sorted.By[Order.Fields.CreatedAt].Desc
    .Then.By[Order.Fields.Id].Asc
    .Page(2).OfSize(50);
```

`Order.Search`, `Order.Rules`, and `Order.Fields` are static extension members
on the entity type. They MUST NOT require the entity to inherit from a library
type or expose an Entity Framework `DbSet`.

The generator MUST include effective inherited public readable entity members
in `Order.Fields`, preferring the most-derived declaration when a member is
overridden or hidden. It MUST diagnose entity members named `Search`, `Rules`,
or `Fields` that would hide an inferred entry point. Indexers, static members,
and non-public getters MUST NOT become generated fields. Generated field types
MUST preserve nullable annotations at every nested type position.

An opted-in catalog MUST diagnose existing members named `SearchRoot`,
`RuleCatalog`, `SearchRuleCatalog`, or `FieldCatalog`, because those names are
reserved for its generated search-support types. The diagnostic MUST suppress
search emission so the catalog's ordinary rule language can still compile.

When a field name is hidden by an inherited `object` member on the field-selector
wrapper, including `Equals`, `GetHashCode`, `GetType`, `MemberwiseClone`,
`ReferenceEquals`, or `ToString`, the explicit indexer form
`.Sorted.By[Order.Fields.ToString]` or `.Then.By[Order.Fields.ToString]` MUST
remain available.

A search MUST remain immutable and safe for concurrent reuse. Paging MUST be
unavailable until at least one explicit sort direction has been selected.
`Then` MUST be unavailable until a primary sort exists. A page number and page
size MUST both be positive. Offset arithmetic MUST reject overflow rather than
wrapping.

The first ordering is authoritative. Each `Then` is an ordered tie-breaker and
MUST preserve the previously selected ordering. Reusing a field is allowed and
has the same semantics as repeating the corresponding provider ordering.

The reference result shape is:

```csharp
public sealed class Page<T>
{
    public IReadOnlyList<T> Results { get; }
    public int Number { get; }
    public int Size { get; }
    public int TotalResults { get; }
    public int TotalPages { get; }
}
```

`TotalPages` MUST be zero for zero results and otherwise use ceiling division.
The final page MAY contain fewer results than `Size`.

Search shaping MUST NOT include projection, eager loading, joins, tracking,
split-query flags, global-filter bypasses, provider functions, or an exposed
query object. Those remain repository and infrastructure concerns.

## 15. Provider translation

Provider translation is an infrastructure concern. A provider adapter receives
the high-level rule tree and prepares a provider-specific plan.

The conceptual service-provider interface is:

```csharp
public interface ISpecTranslator<T, TPlan>
{
    Preparation<TPlan> Prepare(Spec<T> specification);
}
```

`Preparation<TPlan>` MUST represent either:

- a successfully prepared plan; or
- one or more structured translation errors.

A translation error MUST identify the unsupported rule or node and its path.
Preparation MUST occur before query execution where the provider permits it.

An Entity Framework adapter may produce an
`Expression<Func<T, bool>>`. Only infrastructure applies that expression to an
`IQueryable<T>`:

```csharp
// Infrastructure implementation only
var preparation = translator.Prepare(specification);
var predicate = preparation.GetPlanOrThrow();

return await dbContext.Orders
    .Where(predicate)
    .ToListAsync(cancellationToken);
```

Neither the application nor the domain layer receives the query, provider, or
translated expression.

The reference relational Entity Framework adapter MUST expose materializing
operations such as `List`, `Any`, and `Count`, not a deferred query. It MUST
preflight the complete composed expression without executing a command. If the
composition cannot be translated, it SHOULD preflight individual leaves to
report each failing rule ID and its exact node path.

### 15.1 Translation failure

An adapter MUST NOT respond to unsupported translation by:

- treating the rule as `false`;
- treating the rule as `true`;
- dropping the unsupported node;
- fetching an unbounded result and applying the rule in memory; or
- changing to client-side evaluation without explicit infrastructure code.

It MUST return or throw a structured translation error before executing the
query.

For a paged search, the adapter MUST:

1. prepare the complete filter and every ordering before executing a command;
2. count the filtered results without applying ordering or paging;
3. apply the complete deterministic ordering;
4. apply the page offset and size; and
5. materialize a `Page<T>`.

An unsupported field selector MUST fail before either the count or page query
executes. The adapter MUST NOT fall back to client-side ordering or paging.
Global query filters remain active, and infrastructure retains ownership of
tracking and other provider-specific behaviour.

### 15.2 Semantics

An adapter MUST document semantic differences it cannot eliminate, including
null comparison, string comparison, collation, date/time, enum, collection, and
numeric behaviour.

Provider adapters MUST include conformance tests comparing in-memory and
provider execution for their supported expression subset.

EF Core global query filters remain additional repository criteria; a passing
specification does not bypass them. A predicate over a navigation is not an
eager-loading instruction. Relational null compensation, configured relational
null semantics, database collations, and provider-specific scalar support MAY
produce intentional differences, which MUST be named by conformance tests.

### 15.3 Preparation and caching

Adapters SHOULD support preparing reusable plans. A cache key MUST be based on a
safe structural representation and provider context, not merely a display name
or rule ID.

Runtime argument values SHOULD be provider parameters rather than embedded
query text. Candidate or captured values MUST NOT appear in logs or cache keys
by default.

## 16. Rendering and observability

`ToString()` SHOULD render concise domain names:

```text
Can ship AND High priority AND NOT Suspended
```

It MUST preserve grouping when needed:

```text
Can ship AND (High priority OR Manual override)
```

A detailed renderer MAY show the underlying tree, rule IDs, and node paths.
Expression bodies and captured values MUST be omitted or redacted by default.

For a named composed rule, concise rendering SHOULD stop at the named boundary.
Detailed rendering MAY expand its children:

```text
Can ship
  AND Paid
  AND Has delivery address
  AND NOT Cancelled
```

Evaluation and translation telemetry SHOULD use stable rule IDs and node paths.
It SHOULD NOT use full candidate serialization.

## 17. Identity and equality

A stable rule ID identifies a domain rule for diagnostics and telemetry. It is
not object equality.

`Spec<T>` MUST NOT promise structural or semantic equality in version 1. Two
separately constructed specifications with the same ID may not have identical
predicates, metadata, or captured parameters.

The generator MUST diagnose different catalog definitions that reuse the same
stable ID when it can prove the conflict. Reusing the same rule instance at
multiple places in a tree is valid; node paths distinguish each occurrence.

## 18. Async rules

Core `Spec<T>` predicates MUST be synchronous and free of I/O. Provider
translation and repository execution may be asynchronous, but the business rule
itself remains a synchronous Boolean expression.

Rules requiring network, file, clock-service, or database I/O are a different
abstraction. A future `AsyncRule<T>` MAY provide async evaluation, but it MUST
NOT be implicitly convertible to `Spec<T>` or participate in the same provider
translation tree.

## 19. Safety and pathological input

The implementation MUST:

- reject null predicates and null child specifications;
- reject blank rule IDs and names;
- avoid unbounded recursion when rendering or evaluating hostile trees;
- detect cycles in any traversal surface that can observe externally supplied
  nodes;
- preserve cancellation in repository/provider APIs; and
- avoid leaking candidate data through exception or diagnostic text.

Because public `Spec<T>` construction is closed and nodes are immutable, cycles
should not arise in normal composition. Generated code SHOULD diagnose direct
recursive catalog definitions where practical.

## 20. Package boundaries

The intended package split is:

- `FluentSpecifications.Core` — immutable rule tree, composition, in-memory
  evaluation, diagnostics, and provider-neutral translation contracts;
- `FluentSpecifications.Generators` — catalog discovery, extension properties,
  extension methods, exposed candidate properties, and analyzer diagnostics;
- `FluentSpecifications.Expressions` — an infrastructure-facing reference
  translator that produces a parameter-rebound
  `Expression<Func<T, bool>>` without exposing `IQueryable`;
- `FluentSpecifications.EntityFrameworkCore` — relational translation
  preflight and materializing execution, for infrastructure use only;
- `FluentSpecifications.Testing` — assertion and conformance helpers; and
- provider adapters such as `FluentSpecifications.EntityFrameworkCore`, used by
  infrastructure only.

`Core` MUST NOT reference a provider adapter. Provider adapters depend inward on
`Core`.

The repository abstraction is intentionally not a required core package. A
small optional repository-contract package MAY be added later if real consumers
demonstrate a common shape.

## 21. Testing requirements

### 21.1 Core law tests

The core test suite MUST cover:

- the Boolean truth table for every connector;
- short-circuit behaviour of `Matches`;
- complete diagnostic behaviour of `Check`;
- error dominance for `And`, `Or`, and `Not`;
- `AllOf([]) == Always` and `AnyOf([]) == Never`;
- associative flattening without loss of named boundaries;
- grouped rendering;
- negation diagnostics;
- null candidates and null children;
- enumeration of aggregate inputs exactly once; and
- concurrent reuse of one immutable specification.

### 21.2 Generator tests

The generator test suite MUST compile and exercise:

```csharp
CanShip.And.HighPriority.AndNot.Suspended
CanShip.And.WorthAtLeast(100m)
CanShip.And(HighPriority.Or.ManualOverride)
order.CanShip
```

It MUST also verify diagnostics for conflicting exposed properties, duplicate
rule signatures, invalid catalogs, and unsupported language versions.

### 21.3 Provider conformance

Every adapter MUST test:

- supported leaf translation;
- nested `And`, `Or`, and `Not`;
- constants and empty aggregates;
- parameterized rules;
- null behaviour;
- representative string, date/time, enum, collection, and numeric cases;
- preparation failure before execution; and
- absence of implicit client-side evaluation.

Search-capable adapters MUST additionally test:

- generated field ordering in both directions;
- stable multi-field tie-breaking;
- one-based page boundaries and a short final page;
- empty results and `TotalPages == 0`;
- invalid page numbers, sizes, and offset overflow;
- count semantics that ignore ordering and paging;
- complete preflight before the first database command;
- cancellation before execution; and
- absence of `IQueryable` from every public search and adapter signature.

Where an in-memory result and provider result differ intentionally, the test
must name and document that difference.

## 22. Explicit non-goals

Version 1 is not:

- a general-purpose repository framework;
- a replacement for provider query APIs inside infrastructure;
- a string rule language;
- a runtime expression parser;
- a validation framework for arbitrary form input;
- an authorization policy engine;
- a mutation or command specification;
- an async workflow engine;
- a container for includes, joins, or projections; or
- a promise that every valid .NET expression can be translated by every
  provider.

The diagnostic model may be useful for validation-like scenarios, but the
library remains centred on reusable Boolean domain rules.

## 23. Version 1 acceptance criteria

Version 1 is acceptable when all of the following are true:

1. The primary examples in section 1 compile without overloaded operators.
2. Zero-argument named rules compose without parentheses.
3. Arbitrary and grouped specifications compose through connector invocation.
4. `[Expose]` produces `if (order.CanShip)` for eligible named rules.
5. `Spec<T>` retains a named, inspectable Boolean tree after every composition.
6. In-memory evaluation short-circuits and never converts errors to failures.
7. Diagnostic evaluation returns structured, safely renderable failures and
   errors.
8. Empty conjunction and disjunction follow Boolean identity laws.
9. No public API in `Core` or `Generators` exposes `IQueryable` or a persistence
   provider type; the relational EF adapter also never accepts or returns an
   `IQueryable`.
10. The reference expression-plan adapter translates every core node without
    `InvocationExpression`; provider-specific adapters reject unsupported
    expressions without fetching and filtering locally.
11. Query shaping remains outside `Spec<T>` and is represented by immutable
    provider-neutral searches.
12. Core, generator, and adapter conformance suites pass.

## Appendix A: Non-normative influences

The design draws lessons from several existing approaches while intentionally
choosing a narrower core:

- Ardalis.Specification: reusable named queries and small repository surfaces;
- Spring Data JPA Specifications: separate criteria from repository execution;
- RulerZ: a provider-neutral rule model with target-specific compilation;
- TypeScript specification-pattern implementations: explicit composite trees
  and readable short-circuit evaluation;
- Happyr Doctrine Specification: repository-owned provider application, while
  illustrating the cost of mixing query modifiers into Boolean rules;
- Konform: structured, accumulated diagnostic results;
- Trellis: async rules as a distinct concern;
- NSpecifications: expression composition, and the risks of capability changing
  with static type; and
- Cedar and OPA: stable rule identity, explicit errors, validation, preparation,
  residual plans, and traceability.

These projects are influences, not compatibility targets. The requirements in
this document take precedence.
