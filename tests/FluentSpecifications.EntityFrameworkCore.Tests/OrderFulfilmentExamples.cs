using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentSpecifications.Examples.OrderFulfilment;
using FluentSpecifications.Expressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static FluentSpecifications.Examples.OrderFulfilment.OrderRules;
using Xunit;

namespace FluentSpecifications.EntityFrameworkCore.Tests;

public sealed class OrderFulfilmentExamples
{
    [Fact]
    public void Domain_example_reads_without_framework_vocabulary()
    {
        var order = new Order
        {
            Paid = true,
            HasDeliveryAddress = true,
            HighPriority = true
        };

        var ready = ShippingExamples.ReadyToShip();

        Assert.True(order.CanShip);
        Assert.True(ShippingExamples.ShouldDispatch(order));
        Assert.True(ready.Matches(order));
    }

    [Fact]
    public async Task Repository_example_executes_the_same_rule_in_sqlite()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        IReadRepository<Order> repository =
            new EntityFrameworkRepository<Order>(database.Context);
        var ready = CanShip.And(HighPriority.Or.ManualOverride).AndNot.Suspended;

        var orders = await repository.ListAsync(ready);

        Assert.Equal([1, 2], orders.Select(order => order.Id).Order().ToArray());
    }

    [Fact]
    public async Task Captured_rule_arguments_become_database_parameters()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var minimumCents = 10_000;
        var expression = new ExpressionSpecTranslator<Order>()
            .Prepare(WorthAtLeast(minimumCents))
            .GetPlanOrThrow();

        var sql = database.Context.Orders.Where(expression).ToQueryString();

        var declaration = Regex.Match(
            sql,
            @"\.param set (?<name>@\S+) 10000",
            RegexOptions.CultureInvariant);
        Assert.True(declaration.Success, sql);
        Assert.Contains(
            $">= {declaration.Groups["name"].Value}",
            sql,
            StringComparison.Ordinal);
    }

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

    [Fact]
    public async Task Multiple_unsupported_leaves_report_their_rule_ids_and_tree_paths()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        database.CommandCounter.Reset();
        var repository = new EntityFrameworkRepository<Order>(database.Context);
        var rule = CustomerNamedIgnoringCase("alice")
            .And(CustomerNamedByDomainMethod("alice"));

        var exception = await Assert.ThrowsAsync<SpecificationTranslationException>(() =>
            repository.ListAsync(rule));

        Assert.Equal(2, exception.Errors.Count);
        Assert.Collection(
            exception.Errors,
            left =>
            {
                Assert.Equal("order.customer-named-ignoring-case", left.RuleId);
                Assert.Equal("$.left", left.NodePath);
            },
            right =>
            {
                Assert.Equal("order.customer-named-by-domain-method", right.RuleId);
                Assert.Equal("$.right", right.NodePath);
            });
        Assert.Equal(0, database.CommandCounter.ReaderExecutions);
    }

    [Fact]
    public async Task Translation_errors_retain_named_and_negated_tree_boundaries()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var repository = new EntityFrameworkRepository<Order>(database.Context);
        var rule = CustomerNamedIgnoringCase("alice")
            .Not
            .Named("order.not-alice", "Not Alice");

        var exception = await Assert.ThrowsAsync<SpecificationTranslationException>(() =>
            repository.ListAsync(rule));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("order.customer-named-ignoring-case", error.RuleId);
        Assert.Equal("$.rule.not", error.NodePath);
    }

    [Fact]
    public async Task Null_inequality_has_matching_clr_and_default_ef_semantics()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var repository = new EntityFrameworkRepository<Order>(database.Context);
        var rule = CustomerReferenceIsNot("BLOCKED");
        var inMemoryIds = database.VisibleSeedOrders
            .Where(rule.Matches)
            .Select(order => order.Id)
            .Order()
            .ToArray();

        var databaseIds = (await repository.ListAsync(rule))
            .Select(order => order.Id)
            .Order()
            .ToArray();

        Assert.Equal(inMemoryIds, databaseIds);
        Assert.Contains(1, databaseIds); // null != "BLOCKED" under compensated semantics
        Assert.DoesNotContain(4, databaseIds);
    }

    [Fact]
    public async Task Relational_null_mode_can_deliberately_diverge_from_clr_semantics()
    {
        await using var database = await ExampleDatabase.CreateAsync(useRelationalNulls: true);
        var repository = new EntityFrameworkRepository<Order>(database.Context);
        var rule = CustomerReferenceIsNot("BLOCKED");

        Assert.True(rule.Matches(database.VisibleSeedOrders.Single(order => order.Id == 1)));

        var databaseIds = (await repository.ListAsync(rule))
            .Select(order => order.Id)
            .ToArray();
        Assert.DoesNotContain(1, databaseIds);
    }

    [Fact]
    public async Task Exact_string_equality_is_provider_collation_dependent()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var repository = new EntityFrameworkRepository<Order>(database.Context);
        var rule = CustomerNamedExactly("alice");

        Assert.DoesNotContain(database.SeedOrders, rule.Matches);
        Assert.Empty(await repository.ListAsync(rule));
    }

    [Fact]
    public async Task Nullable_navigation_requires_an_explicit_guard_for_in_memory_parity()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var repository = new EntityFrameworkRepository<Order>(database.Context);
        var safe = HasCustomerNamed("Alice");
        var unsafeRule = UnsafeCustomerNamed("Alice");
        var orderWithoutCustomer = database.SeedOrders.Single(order => order.Id == 1);

        Assert.False(safe.Matches(orderWithoutCustomer));
        Assert.Throws<SpecificationEvaluationException>(() =>
            unsafeRule.Matches(orderWithoutCustomer));

        var safelyMatched = await repository.ListAsync(safe);
        Assert.Equal(new[] { 3 }, safelyMatched.Select(order => order.Id).ToArray());
        Assert.Null(Assert.Single(safelyMatched).Customer); // A predicate is not an Include.
        Assert.Empty(database.Context.ChangeTracker.Entries<Order>()); // Executor is no-tracking.
        Assert.Equal(
            new[] { 3 },
            (await repository.ListAsync(unsafeRule)).Select(order => order.Id).ToArray());
    }

    [Fact]
    public async Task Global_query_filters_are_additional_repository_criteria()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var repository = new EntityFrameworkRepository<Order>(database.Context);
        var archived = database.SeedOrders.Single(order => order.Id == 5);
        var unrestricted = Spec.Always<Order>();

        Assert.True(unrestricted.Matches(archived));
        Assert.DoesNotContain(await repository.ListAsync(unrestricted), order => order.Id == 5);
    }

    [Fact]
    public async Task Any_materializes_a_boolean_without_leaking_a_query()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var repository = new EntityFrameworkRepository<Order>(database.Context);

        Assert.True(await repository.AnyAsync(CanShip.And.WorthAtLeast(19_000)));
        Assert.False(await repository.AnyAsync(CanShip.And.WorthAtLeast(25_000)));
    }

    [Fact]
    public async Task Empty_aggregates_and_count_have_explicit_database_semantics()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var executor = new RelationalSpecExecutor<Order>(database.Context);

        Assert.Equal(4, await executor.CountAsync(Spec.AllOf<Order>([])));
        Assert.Equal(0, await executor.CountAsync(Spec.AnyOf<Order>([])));
    }

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

    [Fact]
    public async Task Then_preserves_primary_order_and_stabilizes_ties()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var executor = new RelationalSpecExecutor<Order>(database.Context);
        var request = Order.Search
            .Matching.Paid
            .Sorted.By.HighPriority.Desc
            .Then.By.Id.Asc;

        var results = await executor.ListAsync(request);

        Assert.Equal([1, 3, 2], results.Select(order => order.Id));
    }

    [Fact]
    public async Task A_page_beyond_the_end_is_empty_but_keeps_total_metadata()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var executor = new RelationalSpecExecutor<Order>(database.Context);
        var request = Order.Search
            .Matching.Paid
            .Sorted.By.Id.Asc
            .Page(5).OfSize(2);

        var page = await executor.PageAsync(request);

        Assert.Empty(page.Results);
        Assert.Equal(3, page.TotalResults);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(5, page.Number);
    }

    [Fact]
    public async Task An_empty_page_counts_once_and_skips_the_page_query()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        database.CommandCounter.Reset();
        var executor = new RelationalSpecExecutor<Order>(database.Context);
        var request = Search.Matching(Spec.Never<Order>())
            .Sorted.By[Order.Fields.Id].Asc
            .Page(1).OfSize(10);

        var page = await executor.PageAsync(request);

        Assert.Empty(page.Results);
        Assert.Equal(0, page.TotalResults);
        Assert.Equal(0, page.TotalPages);
        Assert.Equal(1, database.CommandCounter.ReaderExecutions);
    }

    [Fact]
    public async Task Count_ignores_ordering_and_paging()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var executor = new RelationalSpecExecutor<Order>(database.Context);
        var request = Order.Search
            .Matching.Paid
            .Sorted.By.CreatedAt.Desc
            .Page(2).OfSize(1);

        Assert.Equal(3, await executor.CountAsync(request));
    }

    [Fact]
    public async Task Page_totals_keep_global_query_filters()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var executor = new RelationalSpecExecutor<Order>(database.Context);
        var request = Order.Search.All
            .Sorted.By.Id.Asc
            .Page(1).OfSize(10);

        var page = await executor.PageAsync(request);

        Assert.Equal(4, page.TotalResults);
        Assert.DoesNotContain(page.Results, order => order.Archived);
    }

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

    [Fact]
    public async Task Unsupported_secondary_sort_reports_its_ordering_position_before_sql()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        database.CommandCounter.Reset();
        var unsupported = SearchField.Define<Order, string>(
            "NormalizedCustomerName",
            order => Normalize(order.CustomerName));
        var request = Order.Search
            .Matching.Paid
            .Sorted.By.Id.Asc
            .Then.By[unsupported].Desc
            .Page(1).OfSize(25);
        var executor = new RelationalSpecExecutor<Order>(database.Context);

        var exception = await Assert.ThrowsAsync<SpecificationTranslationException>(() =>
            executor.PageAsync(request));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("$.sort[1]", error.NodePath);
        Assert.Equal(0, database.CommandCounter.ReaderExecutions);
    }

    [Fact]
    public async Task Cancelled_page_search_does_not_preflight_count_or_query()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        database.CommandCounter.Reset();
        var executor = new RelationalSpecExecutor<Order>(database.Context);
        var request = Order.Search.All
            .Sorted.By.Id.Asc
            .Page(1).OfSize(10);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.PageAsync(request, cancellation.Token));
        Assert.Equal(0, database.CommandCounter.ReaderExecutions);
    }

    [Fact]
    public async Task Cancellation_is_preserved_and_does_not_trigger_local_fallback()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        database.CommandCounter.Reset();
        var repository = new EntityFrameworkRepository<Order>(database.Context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.ListAsync(Paid, cancellation.Token));
        Assert.Equal(0, database.CommandCounter.ReaderExecutions);
    }

    [Fact]
    public async Task Relational_translator_rejects_a_non_relational_context()
    {
        var options = new DbContextOptionsBuilder<DbContext>()
            .UseInMemoryDatabase($"non-relational-{Guid.NewGuid():N}")
            .Options;
        await using var context = new DbContext(options);
        var preparation = new RelationalSpecTranslator<Order>(context).Prepare(Paid);

        Assert.False(preparation.IsSuccess);
        Assert.Equal(
            "ef-core-provider-not-relational",
            Assert.Single(preparation.Errors).Code);
    }

    [Fact]
    public async Task Provider_specific_scalar_limit_is_a_structured_translation_error()
    {
        await using var database = await ExampleDatabase.CreateAsync();
        var repository = new EntityFrameworkRepository<Order>(database.Context);
        var rule = ProviderTimestampBefore(DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<SpecificationTranslationException>(() =>
            repository.ListAsync(rule));

        var error = Assert.Single(exception.Errors);
        Assert.Equal("order.provider-timestamp-before", error.RuleId);
        Assert.Equal("$", error.NodePath);
    }

    [Fact]
    public void Public_ef_adapter_api_never_returns_or_accepts_iqueryable()
    {
        var offendingTypes = typeof(RelationalSpecExecutor<>).Assembly
            .GetExportedTypes()
            .SelectMany(PublicApiTypes)
            .Where(ContainsQueryable)
            .ToArray();

        Assert.Empty(offendingTypes);
    }

    private static IEnumerable<Type> PublicApiTypes(Type type)
    {
        yield return type;

        foreach (var constructor in type.GetConstructors())
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static bool ContainsQueryable(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IQueryable<>))
        {
            return true;
        }

        return type.HasElementType
            ? ContainsQueryable(type.GetElementType()!)
            : type.IsGenericType && type.GetGenericArguments().Any(ContainsQueryable);
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
    {
        public DbSet<Order> Orders => Set<Order>();

        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().HasQueryFilter(order => !order.Archived);
            modelBuilder.Entity<Order>()
                .HasOne(order => order.Customer)
                .WithMany(customer => customer.Orders)
                .HasForeignKey(order => order.CustomerId);
        }
    }

    private sealed class ExampleDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ExampleDatabase(
            SqliteConnection connection,
            OrderDbContext context,
            CommandCounterInterceptor commandCounter,
            IReadOnlyList<Order> seedOrders)
        {
            _connection = connection;
            Context = context;
            CommandCounter = commandCounter;
            SeedOrders = seedOrders;
            VisibleSeedOrders = seedOrders.Where(order => !order.Archived).ToArray();
        }

        public OrderDbContext Context { get; }

        public CommandCounterInterceptor CommandCounter { get; }

        public IReadOnlyList<Order> SeedOrders { get; }

        public IReadOnlyList<Order> VisibleSeedOrders { get; }

        public static async Task<ExampleDatabase> CreateAsync(bool useRelationalNulls = false)
        {
            var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();
            var commandCounter = new CommandCounterInterceptor();
            var options = new DbContextOptionsBuilder<OrderDbContext>()
                .UseSqlite(
                    connection,
                    sqlite => sqlite.UseRelationalNulls(useRelationalNulls))
                .AddInterceptors(commandCounter)
                .Options;
            var context = new OrderDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var customer = new Customer { Id = 1, Name = "Alice" };
            var seedOrders = new[]
            {
                new Order
                {
                    Id = 1,
                    Paid = true,
                    HasDeliveryAddress = true,
                    HighPriority = true,
                    TotalCents = 15_000,
                    CustomerName = "Alice",
                    CustomerReference = null,
                    CreatedAt = DateTime.UtcNow.AddDays(-3),
                    ProviderTimestamp = DateTimeOffset.UtcNow.AddDays(-3)
                },
                new Order
                {
                    Id = 2,
                    Paid = true,
                    HasDeliveryAddress = true,
                    ManualOverride = true,
                    TotalCents = 20_000,
                    CustomerName = "Bob",
                    CustomerReference = "OK",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    ProviderTimestamp = DateTimeOffset.UtcNow.AddDays(-2)
                },
                new Order
                {
                    Id = 3,
                    Paid = true,
                    HasDeliveryAddress = true,
                    HighPriority = true,
                    Suspended = true,
                    TotalCents = 30_000,
                    CustomerName = "Carol",
                    CustomerReference = "OK",
                    Customer = customer,
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    ProviderTimestamp = DateTimeOffset.UtcNow.AddDays(-1)
                },
                new Order
                {
                    Id = 4,
                    CustomerName = "Dave",
                    CustomerReference = "BLOCKED",
                    CreatedAt = DateTime.UtcNow,
                    ProviderTimestamp = DateTimeOffset.UtcNow
                },
                new Order
                {
                    Id = 5,
                    Archived = true,
                    CustomerName = "Archived",
                    CustomerReference = "ARCHIVED",
                    CreatedAt = DateTime.UtcNow,
                    ProviderTimestamp = DateTimeOffset.UtcNow
                }
            };

            context.AddRange(seedOrders);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            return new ExampleDatabase(connection, context, commandCounter, seedOrders);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class CommandCounterInterceptor : DbCommandInterceptor
    {
        public int ReaderExecutions { get; private set; }

        public void Reset() => ReaderExecutions = 0;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderExecutions++;
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderExecutions++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
