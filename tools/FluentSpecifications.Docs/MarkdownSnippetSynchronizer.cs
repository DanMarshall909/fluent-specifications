using System.Text.RegularExpressions;

namespace FluentSpecifications.Docs;

public sealed record SnippetSynchronizationResult(string Content, bool Changed);

public sealed class SnippetSynchronizationException(string message) : Exception(message);

public sealed partial class MarkdownSnippetSynchronizer
{
    public SnippetSynchronizationResult Synchronize(
        string markdown,
        IReadOnlyDictionary<string, string> snippets)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(snippets);

        var synchronized = SymbolCodeFence().Replace(markdown, match =>
        {
            var symbol = match.Groups["symbol"].Value.Trim();
            if (!snippets.TryGetValue(symbol, out var snippet))
            {
                throw new SnippetSynchronizationException(
                    $"No C# declaration was found for requested symbol '{symbol}'.");
            }

            var newline = match.Groups["newline"].Value;
            var normalizedSnippet = snippet.ReplaceLineEndings(newline);
            return string.Join(
                newline,
                match.Groups["opening"].Value,
                normalizedSnippet,
                match.Groups["closing"].Value);
        });

        return new SnippetSynchronizationResult(
            synchronized,
            !string.Equals(markdown, synchronized, StringComparison.Ordinal));
    }

    public IReadOnlyList<string> RequestedSymbols(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        return SymbolCodeFence()
            .Matches(markdown)
            .Select(match => match.Groups["symbol"].Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [GeneratedRegex(
        "^(?<opening>```csharp[^\\r\\n]*\\bsymbol=\"(?<symbol>[^\"\\r\\n]+)\"[^\\r\\n]*)(?<newline>\\r?\\n)[\\s\\S]*?^(?<closing>```[ \\t]*)$",
        RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex SymbolCodeFence();
}
