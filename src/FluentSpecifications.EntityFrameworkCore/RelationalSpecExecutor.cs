using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace FluentSpecifications.EntityFrameworkCore;

public sealed class RelationalSpecExecutor<T>
    where T : class
{
    private readonly DbContext _context;
    private readonly ISpecTranslator<T, Expression<Func<T, bool>>> _translator;

    public RelationalSpecExecutor(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _translator = new RelationalSpecTranslator<T>(context);
    }

    public async Task<IReadOnlyList<T>> ListAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = Prepare(specification);
        return await _context.Set<T>()
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<T>> ListAsync(
        Search<T> search,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(search);

        var query = PrepareSearch(search, includePaging: true);
        return await query
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Page<T>> PageAsync(
        PagedSearch<T> search,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(search);

        var pageQuery = PrepareSearch(search, includePaging: true);
        var filteredQuery = Filter(search.Specification);
        var totalResults = await filteredQuery
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        if (totalResults == 0)
        {
            return new Page<T>(
                [],
                search.Paging!.Number,
                search.Paging.Size,
                totalResults);
        }

        var results = await pageQuery
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new Page<T>(
            results,
            search.Paging!.Number,
            search.Paging.Size,
            totalResults);
    }

    public Task<bool> AnyAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = Prepare(specification);
        return _context.Set<T>().AnyAsync(predicate, cancellationToken);
    }

    public Task<bool> AnyAsync(
        Search<T> search,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(search);
        return Filter(search.Specification).AnyAsync(cancellationToken);
    }

    public Task<int> CountAsync(
        Spec<T> specification,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var predicate = Prepare(specification);
        return _context.Set<T>().CountAsync(predicate, cancellationToken);
    }

    public Task<int> CountAsync(
        Search<T> search,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(search);
        return Filter(search.Specification).CountAsync(cancellationToken);
    }

    private IQueryable<T> PrepareSearch(Search<T> search, bool includePaging)
    {
        var filtered = Filter(search.Specification);
        var ordered = ApplyOrdering(filtered, search.Ordering);
        var shaped = includePaging && search.Paging is not null
            ? ordered.Skip(search.Paging.Offset).Take(search.Paging.Size)
            : ordered;

        try
        {
            _ = shaped.ToQueryString();
            return shaped;
        }
        catch (Exception exception) when (IsTranslationException(exception))
        {
            var errors = PreflightOrdering(filtered, search.Ordering);
            if (errors.Count > 0)
            {
                return Preparation<IQueryable<T>>.Failed(errors).GetPlanOrThrow();
            }

            return Preparation<IQueryable<T>>.Failed(
            [
                new TranslationError(
                    "ef-core-search-translation-failed",
                    "EF Core could not translate the complete search.",
                    "$")
            ]).GetPlanOrThrow();
        }
    }

    private IQueryable<T> Filter(Spec<T> specification)
    {
        var predicate = Prepare(specification);
        return _context.Set<T>()
            .AsNoTracking()
            .Where(predicate);
    }

    private static IQueryable<T> ApplyOrdering(
        IQueryable<T> source,
        IReadOnlyList<SearchSort<T>> ordering)
    {
        var query = source;
        for (var index = 0; index < ordering.Count; index++)
        {
            var item = ordering[index];
            var methodName = (index, item.Direction) switch
            {
                (0, SearchSortDirection.Ascending) => nameof(Queryable.OrderBy),
                (0, SearchSortDirection.Descending) => nameof(Queryable.OrderByDescending),
                (_, SearchSortDirection.Ascending) => nameof(Queryable.ThenBy),
                _ => nameof(Queryable.ThenByDescending)
            };
            var call = Expression.Call(
                typeof(Queryable),
                methodName,
                [typeof(T), item.Field.ValueType],
                query.Expression,
                Expression.Quote(item.Field.Selector));
            query = query.Provider.CreateQuery<T>(call);
        }

        return query;
    }

    private static IReadOnlyList<TranslationError> PreflightOrdering(
        IQueryable<T> filtered,
        IReadOnlyList<SearchSort<T>> ordering)
    {
        var errors = new List<TranslationError>();
        for (var index = 0; index < ordering.Count; index++)
        {
            try
            {
                _ = ApplyOrdering(filtered, [ordering[index]]).ToQueryString();
            }
            catch (Exception exception) when (IsTranslationException(exception))
            {
                errors.Add(new TranslationError(
                    "ef-core-sort-translation-failed",
                    $"EF Core could not translate sort field '{ordering[index].Field.Name}'.",
                    $"$.sort[{index}]"));
            }
        }

        return errors;
    }

    private static bool IsTranslationException(Exception exception) =>
        exception is InvalidOperationException or NotSupportedException;

    private Expression<Func<T, bool>> Prepare(Spec<T> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return _translator.Prepare(specification).GetPlanOrThrow();
    }
}
