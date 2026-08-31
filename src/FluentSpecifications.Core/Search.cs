using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace FluentSpecifications;

public enum SearchSortDirection
{
    Ascending,
    Descending
}

public sealed class SearchField<T>
{
    internal SearchField(string name, LambdaExpression selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(selector);

        Name = name;
        Selector = selector;
        ValueType = selector.ReturnType;
    }

    public string Name { get; }

    public Type ValueType { get; }

    public LambdaExpression Selector { get; }
}

public static class SearchField
{
    public static SearchField<T> Define<T, TValue>(
        string name,
        Expression<Func<T, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new SearchField<T>(name, selector);
    }
}

public sealed class SearchSort<T>
{
    internal SearchSort(SearchField<T> field, SearchSortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(field);
        Field = field;
        Direction = direction;
    }

    public SearchField<T> Field { get; }

    public SearchSortDirection Direction { get; }
}

public sealed class SearchPaging
{
    internal SearchPaging(int number, int size, int offset)
    {
        Number = number;
        Size = size;
        Offset = offset;
    }

    public int Number { get; }

    public int Size { get; }

    public int Offset { get; }
}

public abstract class Search<T>
{
    private readonly ReadOnlyCollection<SearchSort<T>> _ordering;

    internal Search(
        Spec<T> specification,
        IEnumerable<SearchSort<T>> ordering,
        SearchPaging? paging)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(ordering);

        var snapshot = ordering.ToArray();
        if (snapshot.Any(static item => item is null))
        {
            throw new ArgumentException(
                "Search ordering cannot contain null entries.",
                nameof(ordering));
        }

        Specification = specification;
        _ordering = Array.AsReadOnly(snapshot);
        Paging = paging;
    }

    public Spec<T> Specification { get; }

    public IReadOnlyList<SearchSort<T>> Ordering => _ordering;

    public SearchPaging? Paging { get; }
}

public sealed class UnsortedSearch<T> : Search<T>
{
    internal UnsortedSearch(Spec<T> specification)
        : base(specification, [], null)
    {
    }

    public SearchRuleConnector<T> And =>
        right => Search.Matching(Specification.And(right));

    public SearchRuleConnector<T> Or =>
        right => Search.Matching(Specification.Or(right));

    public SearchRuleConnector<T> AndNot =>
        right => Search.Matching(Specification.AndNot(right));

    public SearchRuleConnector<T> OrNot =>
        right => Search.Matching(Specification.OrNot(right));

    public UnsortedSearch<T> Not => Search.Matching(Specification.Not);

    public PrimarySortStart<T> Sorted => new(this);
}

public delegate UnsortedSearch<T> SearchRuleConnector<T>(Spec<T> right);

public sealed class OrderedSearch<T> : Search<T>
{
    internal OrderedSearch(Spec<T> specification, IEnumerable<SearchSort<T>> ordering)
        : base(specification, ordering, null)
    {
    }

    public SecondarySortStart<T> Then => new(this);

    public PageSizeStart<T> Page(int number)
    {
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(number),
                number,
                "A page number must be greater than zero.");
        }

        return new PageSizeStart<T>(this, number);
    }
}

public sealed class PagedSearch<T> : Search<T>
{
    internal PagedSearch(
        Spec<T> specification,
        IEnumerable<SearchSort<T>> ordering,
        SearchPaging paging)
        : base(specification, ordering, paging)
    {
    }
}

public static class Search
{
    public static UnsortedSearch<T> Matching<T>(Spec<T> specification)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return new UnsortedSearch<T>(specification);
    }

    public static UnsortedSearch<T> All<T>() => Matching(Spec.Always<T>());
}

public readonly struct PrimarySortStart<T>
{
    private readonly UnsortedSearch<T> _search;

    internal PrimarySortStart(UnsortedSearch<T> search) => _search = search;

    public PrimaryFieldSelector<T> By => new(_search);
}

public readonly struct PrimaryFieldSelector<T>
{
    private readonly UnsortedSearch<T> _search;

    internal PrimaryFieldSelector(UnsortedSearch<T> search) => _search = search;

    public PrimaryDirectionSelector<T> this[SearchField<T> field]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(field);
            return new PrimaryDirectionSelector<T>(_search, field);
        }
    }
}

public readonly struct PrimaryDirectionSelector<T>
{
    private readonly UnsortedSearch<T> _search;
    private readonly SearchField<T> _field;

    internal PrimaryDirectionSelector(UnsortedSearch<T> search, SearchField<T> field)
    {
        _search = search;
        _field = field;
    }

    public OrderedSearch<T> Asc => Add(SearchSortDirection.Ascending);

    public OrderedSearch<T> Desc => Add(SearchSortDirection.Descending);

    private OrderedSearch<T> Add(SearchSortDirection direction) =>
        new(
            _search.Specification,
            [new SearchSort<T>(_field, direction)]);
}

public readonly struct SecondarySortStart<T>
{
    private readonly OrderedSearch<T> _search;

    internal SecondarySortStart(OrderedSearch<T> search) => _search = search;

    public SecondaryFieldSelector<T> By => new(_search);
}

public readonly struct SecondaryFieldSelector<T>
{
    private readonly OrderedSearch<T> _search;

    internal SecondaryFieldSelector(OrderedSearch<T> search) => _search = search;

    public SecondaryDirectionSelector<T> this[SearchField<T> field]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(field);
            return new SecondaryDirectionSelector<T>(_search, field);
        }
    }
}

public readonly struct SecondaryDirectionSelector<T>
{
    private readonly OrderedSearch<T> _search;
    private readonly SearchField<T> _field;

    internal SecondaryDirectionSelector(OrderedSearch<T> search, SearchField<T> field)
    {
        _search = search;
        _field = field;
    }

    public OrderedSearch<T> Asc => Add(SearchSortDirection.Ascending);

    public OrderedSearch<T> Desc => Add(SearchSortDirection.Descending);

    private OrderedSearch<T> Add(SearchSortDirection direction) =>
        new(
            _search.Specification,
            [.. _search.Ordering, new SearchSort<T>(_field, direction)]);
}

public readonly struct PageSizeStart<T>
{
    private readonly OrderedSearch<T> _search;
    private readonly int _number;

    internal PageSizeStart(OrderedSearch<T> search, int number)
    {
        _search = search;
        _number = number;
    }

    public PagedSearch<T> OfSize(int size)
    {
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "A page size must be greater than zero.");
        }

        var offset = (_number - 1L) * size;
        if (offset > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "The requested page offset exceeds the supported range.");
        }

        return new PagedSearch<T>(
            _search.Specification,
            _search.Ordering,
            new SearchPaging(_number, size, (int)offset));
    }
}

public sealed class Page<T>
{
    private readonly ReadOnlyCollection<T> _results;

    public Page(
        IEnumerable<T> results,
        int number,
        int size,
        int totalResults)
    {
        ArgumentNullException.ThrowIfNull(results);
        if (number <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(number));
        }

        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (totalResults < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalResults));
        }

        var snapshot = results.ToArray();
        if (snapshot.Length > size)
        {
            throw new ArgumentException(
                "A page cannot contain more results than its configured size.",
                nameof(results));
        }

        _results = Array.AsReadOnly(snapshot);
        Number = number;
        Size = size;
        TotalResults = totalResults;
        TotalPages = totalResults == 0
            ? 0
            : checked((int)((totalResults + (long)size - 1L) / size));
    }

    public IReadOnlyList<T> Results => _results;

    public int Number { get; }

    public int Size { get; }

    public int TotalResults { get; }

    public int TotalPages { get; }
}
