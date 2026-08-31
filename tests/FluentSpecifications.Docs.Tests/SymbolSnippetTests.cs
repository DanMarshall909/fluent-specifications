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
}
