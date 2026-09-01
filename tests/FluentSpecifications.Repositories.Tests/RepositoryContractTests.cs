using System.Collections;
using System.Reflection;
using Xunit;

namespace FluentSpecifications.Repositories.Tests;

public sealed class RepositoryContractTests
{
    [Fact]
    public void Repository_contract_is_provider_neutral_and_does_not_expose_iqueryable()
    {
        var assembly = typeof(IReadRepository<>).Assembly;
        var providerReferences = assembly.GetReferencedAssemblies()
            .Where(reference => reference.Name?.Contains(
                "EntityFrameworkCore",
                StringComparison.Ordinal) is true)
            .ToArray();
        var queryableSignatures = typeof(IReadRepository<>).GetMethods()
            .SelectMany(method =>
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .Append(method.ReturnType))
            .Where(ContainsQueryable)
            .ToArray();

        Assert.Empty(providerReferences);
        Assert.Empty(queryableSignatures);
    }

    [Fact]
    public async Task A_non_ef_provider_can_implement_the_contract_for_value_types()
    {
        IReadRepository<int> repository =
            new InMemoryReadRepository<int>([1, 2, 3, 4]);
        var even = Spec.Define<int>("number.even", "Even", number => number % 2 == 0);
        var search = Search.Matching(even)
            .Sorted.By[SearchField.Define<int, int>("Value", number => number)].Desc
            .Page(1).OfSize(1);

        Assert.Equal([2, 4], await repository.ListAsync(even));
        Assert.Equal([4], (await repository.PageAsync(search)).Results);
        Assert.True(await repository.AnyAsync(even));
        Assert.Equal(2, await repository.CountAsync(even));
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

    private sealed class InMemoryReadRepository<T>(IEnumerable<T> candidates) : IReadRepository<T>
    {
        private readonly IReadOnlyList<T> _candidates = candidates.ToArray();

        public Task<IReadOnlyList<T>> ListAsync(
            Spec<T> specification,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(specification);
            return Task.FromResult<IReadOnlyList<T>>(
                _candidates.Where(specification.Matches).ToArray());
        }

        public Task<IReadOnlyList<T>> ListAsync(
            Search<T> search,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(search);
            return Task.FromResult<IReadOnlyList<T>>(Execute(search).ToArray());
        }

        public Task<Page<T>> PageAsync(
            PagedSearch<T> search,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(search);
            var totalResults = _candidates.Count(search.Specification.Matches);
            return Task.FromResult(new Page<T>(
                Execute(search).ToArray(),
                search.Paging!.Number,
                search.Paging.Size,
                totalResults));
        }

        public Task<bool> AnyAsync(
            Spec<T> specification,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(specification);
            return Task.FromResult(_candidates.Any(specification.Matches));
        }

        public Task<bool> AnyAsync(
            Search<T> search,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(search);
            return Task.FromResult(_candidates.Any(search.Specification.Matches));
        }

        public Task<int> CountAsync(
            Spec<T> specification,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(specification);
            return Task.FromResult(_candidates.Count(specification.Matches));
        }

        public Task<int> CountAsync(
            Search<T> search,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(search);
            return Task.FromResult(_candidates.Count(search.Specification.Matches));
        }

        private IEnumerable<T> Execute(Search<T> search)
        {
            IEnumerable<T> result = _candidates.Where(search.Specification.Matches);
            IOrderedEnumerable<T>? ordered = null;

            foreach (var sort in search.Ordering)
            {
                var compiled = sort.Field.Selector.Compile();
                Func<T, object?> keySelector = candidate => compiled.DynamicInvoke(candidate);
                ordered = (ordered, sort.Direction) switch
                {
                    (null, SearchSortDirection.Ascending) =>
                        result.OrderBy(keySelector, ComparableObjectComparer.Instance),
                    (null, SearchSortDirection.Descending) =>
                        result.OrderByDescending(keySelector, ComparableObjectComparer.Instance),
                    (_, SearchSortDirection.Ascending) =>
                        ordered!.ThenBy(keySelector, ComparableObjectComparer.Instance),
                    _ => ordered!.ThenByDescending(keySelector, ComparableObjectComparer.Instance)
                };
                result = ordered;
            }

            return search.Paging is null
                ? result
                : result.Skip(search.Paging.Offset).Take(search.Paging.Size);
        }
    }

    private sealed class ComparableObjectComparer : IComparer<object?>
    {
        public static ComparableObjectComparer Instance { get; } = new();

        public int Compare(object? x, object? y) =>
            Comparer.DefaultInvariant.Compare(x, y);
    }
}
