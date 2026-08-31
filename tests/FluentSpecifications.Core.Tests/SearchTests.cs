using System.Reflection;
using Xunit;

namespace FluentSpecifications.Core.Tests;

public sealed class SearchTests
{
    private static readonly Spec<Example> Active =
        Spec.Define<Example>("example.active", "Active", example => example.Active);

    private static readonly SearchField<Example> Id =
        SearchField.Define<Example, int>("Id", example => example.Id);

    private static readonly SearchField<Example> Score =
        SearchField.Define<Example, decimal>("Score", example => example.Score);

    [Fact]
    public void Search_shaping_is_immutable_and_keeps_rules_separate()
    {
        var matching = Search.Matching(Active);
        var sorted = matching.Sorted.By[Score].Desc;
        var tied = sorted.Then.By[Id].Asc;
        var paged = tied.Page(2).OfSize(25);

        Assert.Same(Active, matching.Specification);
        Assert.Empty(matching.Ordering);
        Assert.Null(matching.Paging);

        Assert.Single(sorted.Ordering);
        Assert.Equal(SearchSortDirection.Descending, sorted.Ordering[0].Direction);

        Assert.Equal(2, tied.Ordering.Count);
        Assert.Equal("Id", tied.Ordering[1].Field.Name);
        Assert.Equal(SearchSortDirection.Ascending, tied.Ordering[1].Direction);

        Assert.Equal(2, paged.Paging!.Number);
        Assert.Equal(25, paged.Paging.Size);
        Assert.Equal(25, paged.Paging.Offset);
        Assert.Same(Active, paged.Specification);
    }

    [Fact]
    public void Matching_rules_continue_with_the_same_boolean_vocabulary()
    {
        var highScore = Spec.Define<Example>(
            "example.high-score",
            "High score",
            example => example.Score >= 10m);

        var search = Search.Matching(Active)
            .And(highScore)
            .OrNot(Spec.Never<Example>());

        Assert.True(search.Specification.Matches(new Example(1, true, 12m)));
        Assert.False(
            Search.Matching(Active)
                .And(highScore)
                .Specification
                .Matches(new Example(1, false, 12m)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_numbers_must_be_positive(int number)
    {
        var sorted = Search.Matching(Active).Sorted.By[Id].Asc;

        Assert.Throws<ArgumentOutOfRangeException>(() => sorted.Page(number));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Page_sizes_must_be_positive(int size)
    {
        var page = Search.Matching(Active).Sorted.By[Id].Asc.Page(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => page.OfSize(size));
    }

    [Fact]
    public void Page_offset_must_not_overflow_provider_integer_paging()
    {
        var page = Search.Matching(Active)
            .Sorted.By[Id].Asc
            .Page(int.MaxValue);

        Assert.Throws<ArgumentOutOfRangeException>(() => page.OfSize(2));
    }

    [Fact]
    public void Paging_and_secondary_sorting_are_unavailable_before_primary_ordering()
    {
        Assert.Null(typeof(UnsortedSearch<Example>).GetMethod("Page"));
        Assert.Null(typeof(UnsortedSearch<Example>).GetProperty("Then"));
        Assert.NotNull(typeof(OrderedSearch<Example>).GetMethod("Page"));
        Assert.NotNull(typeof(OrderedSearch<Example>).GetProperty("Then"));
    }

    [Fact]
    public void One_search_can_be_reused_concurrently_without_mutation()
    {
        var search = Search.Matching(Active)
            .Sorted.By[Score].Desc
            .Then.By[Id].Asc
            .Page(2).OfSize(10);

        Parallel.For(0, 100, _ =>
        {
            Assert.Equal(2, search.Ordering.Count);
            Assert.Equal(10, search.Paging!.Size);
            Assert.True(search.Specification.Matches(new Example(1, true, 10m)));
        });
    }

    [Fact]
    public void Search_fields_reject_blank_names_and_null_selectors()
    {
        Assert.Throws<ArgumentException>(() =>
            SearchField.Define<Example, int>(" ", example => example.Id));
        Assert.Throws<ArgumentNullException>(() =>
            SearchField.Define<Example, int>("Id", null!));
    }

    [Fact]
    public void Dynamic_field_selectors_reject_null_fields()
    {
        var matching = Search.Matching(Active);
        var ordered = matching.Sorted.By[Id].Asc;

        Assert.Throws<ArgumentNullException>(() =>
            _ = matching.Sorted.By[null!]);
        Assert.Throws<ArgumentNullException>(() =>
            _ = ordered.Then.By[null!]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(50, 1)]
    [InlineData(51, 2)]
    [InlineData(100, 2)]
    [InlineData(101, 3)]
    public void Page_reports_ceiling_divided_total_pages(int totalResults, int totalPages)
    {
        var page = new Page<Example>([], number: 1, size: 50, totalResults);

        Assert.Equal(totalPages, page.TotalPages);
    }

    [Fact]
    public void A_page_cannot_contain_more_results_than_its_size()
    {
        Assert.Throws<ArgumentException>(() =>
            new Page<Example>(
                [new Example(1, true, 1m), new Example(2, true, 2m)],
                number: 1,
                size: 1,
                totalResults: 2));
    }

    [Fact]
    public void Public_core_search_api_does_not_expose_iqueryable()
    {
        var searchTypes = typeof(Search<>).Assembly
            .GetExportedTypes()
            .Where(type => type.Name.Contains("Search", StringComparison.Ordinal) ||
                type.Name.StartsWith("Page", StringComparison.Ordinal))
            .SelectMany(PublicApiTypes)
            .Where(ContainsQueryable)
            .ToArray();

        Assert.Empty(searchTypes);
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

        foreach (var property in type.GetProperties())
        {
            yield return property.PropertyType;
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

    private sealed record Example(int Id, bool Active, decimal Score);
}
