---
title: Testing the behavior
description: Read the executable examples that define fluent syntax, Boolean laws, diagnostics, expression composition, generator behavior, and EF Core boundaries.
order: 6
section: Confidence
---

The test suite is the behavioral contract. The documentation extracts its code
directly from those tests through Roslyn, so a renamed or removed symbol fails
the documentation build instead of leaving a stale copy behind.

## Fluent syntax examples

The generator integration suite compiles the syntax users actually write. This
is the contract for a zero-argument chain:

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

Separate tests cover grouped connector invocation, exposed domain properties,
optional arguments, caching, readonly fields, overloads, `params`, and escaped
keyword parameter names.

The `Zero_argument_rules_compose_without_parentheses` test is intentionally
named in user language: parentheses should appear only when they communicate
grouping or carry arguments.

## Core Boolean laws

The core suite verifies truth tables, left-to-right short-circuiting, negation,
constants, empty aggregates, single enumeration, null rejection, immutable
snapshots, rendering, and concurrent reuse.

Short-circuiting is observed rather than inferred:

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

That method—`Matches_short_circuits_and_from_left_to_right`—protects behavior
that could easily change during an internal tree rewrite.

## Diagnostics as data

Diagnostic tests assert the outcome and every important field rather than
snapshotting a formatted string:

```csharp symbol="M:FluentSpecifications.Core.Tests.DiagnosticTests.Check_returns_a_structured_business_failure"
[Fact]
public void Check_returns_a_structured_business_failure()
{
    var paid = Spec.Define<Order>(
        id: "order.paid",
        name: "Paid",
        predicate: order => order.Paid,
        failure: "Payment has not been received.",
        code: "payment-required",
        path: "PaymentStatus");

    var result = paid.Check(new Order());

    Assert.Equal(CheckOutcome.Failed, result.Outcome);
    Assert.False(result.Passed);
    Assert.True(result.IsComplete);
    Assert.Empty(result.Errors);

    var ruleFailure = Assert.Single(result.Failures);
    Assert.Equal(RuleFailureKind.Rule, ruleFailure.Kind);
    Assert.Equal("order.paid", ruleFailure.RuleId);
    Assert.Equal("Paid", ruleFailure.Name);
    Assert.Equal("Payment has not been received.", ruleFailure.Message);
    Assert.Equal("payment-required", ruleFailure.Code);
    Assert.Equal("PaymentStatus", ruleFailure.Path);
    Assert.Equal("$", ruleFailure.NodePath);
    Assert.Empty(ruleFailure.Causes);
}
```

They also cover error dominance, complete versus short-circuit evaluation,
grouped alternatives, named boundaries, safe negation messages, immutable
collections, and snapshotted context.

## Expression-tree safety

The expression adapter must rebind parameters without creating
`InvocationExpression`, including when a leaf contains a nested lambda:

```csharp symbol="M:FluentSpecifications.Expressions.Tests.ExpressionSpecTranslatorTests.Prepared_expression_preserves_nested_lambda_parameters"
[Fact]
public void Prepared_expression_preserves_nested_lambda_parameters()
{
    var hasMatchingTag = Rule(
        "matching-tag",
        order => order.Tags.Any(tag => tag == order.ExpectedTag));
    var paid = Rule("paid", candidate => candidate.Paid);

    var expression = new ExpressionSpecTranslator<Order>()
        .Prepare(paid.And(hasMatchingTag))
        .GetPlanOrThrow();
    var predicate = expression.Compile();

    Assert.True(predicate(new Order(Paid: true, ExpectedTag: "urgent", TagValues: ["urgent"])));
    Assert.False(predicate(new Order(Paid: true, ExpectedTag: "urgent", TagValues: ["normal"])));
    Assert.False(ContainsInvocation(expression));
}
```

That shape matters because many query providers cannot translate invocation
nodes even when the underlying predicates would otherwise be supported.

## Relational EF behavior

The EF examples run the same order rules against objects and SQLite. The most
important failure contract proves that unsupported translation happens before
any `SELECT` command:

```csharp symbol="M:FluentSpecifications.EntityFrameworkCore.Tests.OrderFulfilmentExamples.Unsupported_filter_fails_before_any_select_is_executed"
[Fact]
public async Task Unsupported_filter_fails_before_any_select_is_executed()
{
    await using var database = await ExampleDatabase.CreateAsync();
    database.CommandCounter.Reset();
    var repository = new EntityFrameworkRepository<Order>(database.Context);
    var inMemoryCandidate = new Order { CustomerName = "ALICE" };
    var rule = CustomerNamedIgnoringCase("alice");

    Assert.True(rule.Matches(inMemoryCandidate));

    var exception = await Assert.ThrowsAsync<SpecificationTranslationException>(() =>
        repository.ListAsync(rule));

    var error = Assert.Single(exception.Errors);
    Assert.Equal("ef-core-translation-failed", error.Code);
    Assert.Equal("order.customer-named-ignoring-case", error.RuleId);
    Assert.Equal("$", error.NodePath);
    Assert.Equal(0, database.CommandCounter.ReaderExecutions);
}
```

The suite names intentional differences around null configuration, collations,
nullable navigations, global filters, and SQLite scalar support. It also proves
cancellation, parameterization, no-tracking materialization, constants, and the
absence of an `IQueryable` public surface through
`Public_ef_adapter_api_never_returns_or_accepts_iqueryable`.

The happy path asserts filtering, ordered tie-breaking, one-based paging, and
the total metadata returned from the real SQLite adapter:

```csharp symbol="M:FluentSpecifications.EntityFrameworkCore.Tests.OrderFulfilmentExamples.Fluent_search_filters_sorts_and_returns_page_metadata"
[Fact]
public async Task Fluent_search_filters_sorts_and_returns_page_metadata()
{
    await using var database = await ExampleDatabase.CreateAsync();
    var repository = new EntityFrameworkRepository<Order>(database.Context);
    var request = Order.Search
        .Matching.Paid
        .Sorted.By.CreatedAt.Desc
        .Then.By.Id.Asc
        .Page(2).OfSize(1);

    var page = await repository.PageAsync(request);

    Assert.Equal([2], page.Results.Select(order => order.Id));
    Assert.Equal(2, page.Number);
    Assert.Equal(1, page.Size);
    Assert.Equal(3, page.TotalResults);
    Assert.Equal(3, page.TotalPages);
}
```

The failure path proves an unsupported field is rejected before either the
count or page query reaches the database:

```csharp symbol="M:FluentSpecifications.EntityFrameworkCore.Tests.OrderFulfilmentExamples.Unsupported_sort_fails_before_count_or_page_commands_execute"
[Fact]
public async Task Unsupported_sort_fails_before_count_or_page_commands_execute()
{
    await using var database = await ExampleDatabase.CreateAsync();
    database.CommandCounter.Reset();
    var unsupported = SearchField.Define<Order, string>(
        "NormalizedCustomerName",
        order => Normalize(order.CustomerName));
    var request = Search.Matching(Paid)
        .Sorted.By[unsupported].Asc
        .Page(1).OfSize(25);
    var executor = new RelationalSpecExecutor<Order>(database.Context);

    var exception = await Assert.ThrowsAsync<SpecificationTranslationException>(() =>
        executor.PageAsync(request));

    var error = Assert.Single(exception.Errors);
    Assert.Equal("ef-core-sort-translation-failed", error.Code);
    Assert.Equal("$.sort[0]", error.NodePath);
    Assert.Equal(0, database.CommandCounter.ReaderExecutions);
}
```

## Repository providers share one contract

The repository contract lives outside EF Core, has no provider dependency, and
does not constrain candidates to reference types. A small in-memory provider in
the contract suite proves that a value type can implement and execute the same
materializing surface:

```csharp symbol="M:FluentSpecifications.Repositories.Tests.RepositoryContractTests.A_non_ef_provider_can_implement_the_contract_for_value_types"
[Fact]
public async Task A_non_ef_provider_can_implement_the_contract_for_value_types()
{
    IReadRepository<int> repository = new InMemoryReadRepository<int>([1, 2, 3, 4]);
    var even = Spec.Define<int>("number.even", "Even", number => number % 2 == 0);
    var search = Search.Matching(even)
        .Sorted.By[SearchField.Define<int, int>("Value", number => number)].Desc
        .Page(1).OfSize(1);

    Assert.Equal([2, 4], await repository.ListAsync(even));
    Assert.Equal([4], (await repository.PageAsync(search)).Results);
    Assert.True(await repository.AnyAsync(even));
    Assert.Equal(2, await repository.CountAsync(even));
}
```

## Documentation snippets are tested too

Each fence carries a Roslyn documentation ID in its metadata. The extractor
uses that canonical symbol to disambiguate overloads:

```csharp symbol="M:FluentSpecifications.Docs.Tests.SymbolSnippetTests.Extractor_uses_roslyn_documentation_ids_and_disambiguates_overloads"
[Fact]
public void Extractor_uses_roslyn_documentation_ids_and_disambiguates_overloads()
{
    const string source = """
        namespace Examples;

        public sealed class Rules
        {
            public bool Ready => true;

            public bool Match(int value) => value > 0;

            public bool Match(string value) => value.Length > 0;

            public bool Cancel(CancellationToken token) => token.IsCancellationRequested;
        }
        """;
    var extractor = new SymbolSnippetExtractor();

    var snippets = extractor.Extract(
    [
        new SourceDocument("Rules.cs", source)
    ]);

    Assert.Equal(
        "public bool Ready => true;",
        snippets["P:Examples.Rules.Ready"]);
    Assert.Equal(
        "public bool Match(int value) => value > 0;",
        snippets["M:Examples.Rules.Match(System.Int32)"]);
    Assert.Equal(
        "public bool Match(string value) => value.Length > 0;",
        snippets["M:Examples.Rules.Match(System.String)"]);
    Assert.Equal(
        "public bool Cancel(CancellationToken token) => token.IsCancellationRequested;",
        snippets["M:Examples.Rules.Cancel(System.Threading.CancellationToken)"]);
}
```

The synchronizer fails for missing symbols, duplicate declarations, or stale
generated output. The landing-page sample comes from the same generated snippet
catalog, so there is no separate marketing-only code path.

## Run the checks

Run `dotnet test FluentSpecifications.slnx --configuration Release --no-restore
-m:1 -nr:false` for the library, generator, Roslyn documentation tool, and EF
examples. Run `npm test` for snippet freshness, Markdown contracts, the Astro
production build, metadata, custom-domain artifacts, and internal links.

SQLite coverage should be supplemented by the consuming application's actual
production provider. A green fake-provider suite is evidence, not a guarantee.
