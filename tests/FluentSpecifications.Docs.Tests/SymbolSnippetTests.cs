using FluentSpecifications.Docs;
using Xunit;

namespace FluentSpecifications.Docs.Tests;

public sealed class SymbolSnippetTests
{
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

    [Fact]
    public void Synchronizer_reads_the_roslyn_symbol_from_code_fence_metadata()
    {
        const string markdown = """
            Before.

            ```csharp symbol="P:Examples.Rules.Ready"
            stale copy
            ```

            After.
            """;
        var synchronizer = new MarkdownSnippetSynchronizer();

        var result = synchronizer.Synchronize(
            markdown,
            new Dictionary<string, string>
            {
                ["P:Examples.Rules.Ready"] = "public bool Ready => true;"
            });

        Assert.True(result.Changed);
        Assert.Contains("public bool Ready => true;", result.Content);
        Assert.DoesNotContain("stale copy", result.Content);
        Assert.False(synchronizer.Synchronize(result.Content, new Dictionary<string, string>
        {
            ["P:Examples.Rules.Ready"] = "public bool Ready => true;"
        }).Changed);
    }

    [Fact]
    public void Synchronizer_reports_a_missing_symbol_by_its_requested_name()
    {
        const string markdown = """
            ```csharp symbol="M:Examples.Rules.Missing"
            ```
            """;
        var synchronizer = new MarkdownSnippetSynchronizer();

        var exception = Assert.Throws<SnippetSynchronizationException>(() =>
            synchronizer.Synchronize(markdown, new Dictionary<string, string>()));

        Assert.Contains("M:Examples.Rules.Missing", exception.Message);
    }

    [Fact]
    public void Extractor_adds_parameter_hints_for_positional_arguments()
    {
        const string source = """
            namespace Examples;

            public static class Examples
            {
                public static bool Between(int value, int minimum, int maximum) =>
                    value >= minimum && value <= maximum;

                public static bool IsUseful() => Between(42, 10, 100);
            }
            """;
        var extractor = new SymbolSnippetExtractor();

        var snippet = extractor.ExtractDetailed(
        [
            new SourceDocument("Examples.cs", source)
        ])["M:Examples.Examples.IsUseful"];

        Assert.Equal("public static bool IsUseful() => Between(42, 10, 100);", snippet.Code);
        Assert.Collection(
            snippet.ParameterHints,
            hint => AssertHint(snippet.Code, hint, "value", "42"),
            hint => AssertHint(snippet.Code, hint, "minimum", "10"),
            hint => AssertHint(snippet.Code, hint, "maximum", "100"));
    }

    [Fact]
    public void Extractor_omits_hints_that_would_repeat_named_or_matching_arguments()
    {
        const string source = """
            namespace Examples;

            public static class Examples
            {
                public static bool Between(int value, int minimum, int maximum) => true;

                public static bool Forward(int value) =>
                    Between(value, minimum: 10, 100);
            }
            """;
        var extractor = new SymbolSnippetExtractor();

        var snippet = extractor.ExtractDetailed(
        [
            new SourceDocument("Examples.cs", source)
        ])["M:Examples.Examples.Forward(System.Int32)"];

        var hint = Assert.Single(snippet.ParameterHints);
        AssertHint(snippet.Code, hint, "maximum", "100");
    }

    [Fact]
    public void Extractor_uses_the_resolved_overload_and_hints_a_params_parameter_once()
    {
        const string source = """
            namespace Examples;

            public static class Examples
            {
                public static string Format(string message) => message;
                public static string Format(int code) => code.ToString();
                public static string Join(string separator, params string[] values) => "";

                public static string Render() =>
                    Format(404) + Join(", ", "first", "second");
            }
            """;
        var extractor = new SymbolSnippetExtractor();

        var snippet = extractor.ExtractDetailed(
        [
            new SourceDocument("Examples.cs", source)
        ])["M:Examples.Examples.Render"];

        Assert.Collection(
            snippet.ParameterHints,
            hint => AssertHint(snippet.Code, hint, "code", "404"),
            hint => AssertHint(snippet.Code, hint, "separator", "\", \""),
            hint => AssertHint(snippet.Code, hint, "values", "\"first\""));
    }

    [Fact]
    public void Extractor_can_select_a_named_local_from_its_parent_symbol()
    {
        const string source = """
            namespace Examples;

            public static class Searches
            {
                public static int Build()
                {
                    var request = Math.Clamp(50, 1, 100);
                    return request;
                }
            }
            """;
        var extractor = new SymbolSnippetExtractor();

        var snippet = extractor.ExtractDetailed(
        [
            new SourceDocument("Searches.cs", source)
        ])["M:Examples.Searches.Build|local:request"];

        Assert.Equal("var request = Math.Clamp(50, 1, 100);", snippet.Code);
        Assert.Collection(
            snippet.ParameterHints,
            hint => AssertHint(snippet.Code, hint, "value", "50"),
            hint => AssertHint(snippet.Code, hint, "min", "1"),
            hint => AssertHint(snippet.Code, hint, "max", "100"));
    }

    [Fact]
    public void Extractor_uses_unique_roslyn_method_symbols_when_a_generated_receiver_is_unresolved()
    {
        const string source = """
            namespace Examples;

            public sealed class PageBuilder
            {
                public PageBuilder Page(int number) => this;
                public PageBuilder OfSize(int size) => this;
            }

            public static class Searches
            {
                public static void Build()
                {
                    var request = Generated.Order.Search.Page(2).OfSize(50);
                }
            }
            """;
        var extractor = new SymbolSnippetExtractor();

        var snippet = extractor.ExtractDetailed(
        [
            new SourceDocument("Searches.cs", source)
        ])["M:Examples.Searches.Build|local:request"];

        Assert.Collection(
            snippet.ParameterHints,
            hint => AssertHint(snippet.Code, hint, "number", "2"),
            hint => AssertHint(snippet.Code, hint, "size", "50"));
    }

    private static void AssertHint(
        string code,
        ParameterHint hint,
        string expectedName,
        string expectedArgument)
    {
        Assert.Equal(expectedName, hint.Name);
        Assert.StartsWith(expectedArgument, code[hint.Offset..], StringComparison.Ordinal);
    }
}
