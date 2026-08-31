using System.Text.Json;
using FluentSpecifications.Docs;

return await DocumentationCli.RunAsync(args);

internal static class DocumentationCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var mode = args.FirstOrDefault() ?? "check";
            if (mode is not ("sync" or "check" or "list"))
            {
                throw new SnippetSynchronizationException(
                    "Usage: dotnet run --project tools/FluentSpecifications.Docs -- [sync|check|list] [filter]");
            }

            var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
            var documentationFiles = Directory
                .EnumerateFiles(
                    Path.Combine(root, "src", "content", "docs"),
                    "*.md",
                    SearchOption.AllDirectories)
                .Append(Path.Combine(root, "README.md"))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var synchronizer = new MarkdownSnippetSynchronizer();
            var requestedSymbols = documentationFiles
                .SelectMany(path => synchronizer.RequestedSymbols(File.ReadAllText(path)))
                .Concat(ReadSiteSymbols(root))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var allSnippets = new SymbolSnippetExtractor().ExtractDetailed(ReadSources(root));
            if (mode == "list")
            {
                var filter = args.ElementAtOrDefault(1);
                foreach (var symbol in allSnippets.Keys
                    .Where(symbol => filter is null || symbol.Contains(
                        filter,
                        StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal))
                {
                    Console.WriteLine(symbol);
                }

                return 0;
            }

            var requestedDetails = requestedSymbols.ToDictionary(
                symbol => symbol,
                symbol => allSnippets.TryGetValue(symbol, out var snippet)
                    ? snippet
                    : throw new SnippetSynchronizationException(
                        $"No C# declaration was found for requested symbol '{symbol}'."),
                StringComparer.Ordinal);
            var requestedSnippets = requestedDetails.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Code,
                StringComparer.Ordinal);

            var staleFiles = new List<string>();
            foreach (var path in documentationFiles)
            {
                var existing = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                var result = synchronizer.Synchronize(existing, requestedSnippets);
                if (!result.Changed)
                {
                    continue;
                }

                if (mode == "sync")
                {
                    await File.WriteAllTextAsync(path, result.Content).ConfigureAwait(false);
                }
                else
                {
                    staleFiles.Add(Path.GetRelativePath(root, path));
                }
            }

            var generatedPath = Path.Combine(root, "src", "generated", "snippets.json");
            var generatedJson = JsonSerializer.Serialize(
                requestedSnippets,
                new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
            var existingJson = File.Exists(generatedPath)
                ? await File.ReadAllTextAsync(generatedPath).ConfigureAwait(false)
                : null;
            if (!string.Equals(existingJson, generatedJson, StringComparison.Ordinal))
            {
                if (mode == "sync")
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
                    await File.WriteAllTextAsync(generatedPath, generatedJson).ConfigureAwait(false);
                }
                else
                {
                    staleFiles.Add(Path.GetRelativePath(root, generatedPath));
                }
            }

            var parameterHintsPath = Path.Combine(
                root,
                "src",
                "generated",
                "parameter-hints.json");
            var parameterHints = requestedDetails.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.ParameterHints,
                StringComparer.Ordinal);
            var parameterHintsJson = JsonSerializer.Serialize(
                parameterHints,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }) + Environment.NewLine;
            var existingParameterHintsJson = File.Exists(parameterHintsPath)
                ? await File.ReadAllTextAsync(parameterHintsPath).ConfigureAwait(false)
                : null;
            if (!string.Equals(
                existingParameterHintsJson,
                parameterHintsJson,
                StringComparison.Ordinal))
            {
                if (mode == "sync")
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(parameterHintsPath)!);
                    await File.WriteAllTextAsync(
                        parameterHintsPath,
                        parameterHintsJson).ConfigureAwait(false);
                }
                else
                {
                    staleFiles.Add(Path.GetRelativePath(root, parameterHintsPath));
                }
            }

            if (staleFiles.Count > 0)
            {
                Console.Error.WriteLine(
                    $"Generated documentation is stale: {string.Join(", ", staleFiles)}");
                Console.Error.WriteLine("Run 'npm run snippets:sync' and commit the result.");
                return 1;
            }

            Console.WriteLine(
                mode == "sync"
                    ? $"Synchronized {requestedSnippets.Count} symbol snippets."
                    : $"Verified {requestedSnippets.Count} symbol snippets.");
            return 0;
        }
        catch (Exception exception) when (
            exception is SnippetSynchronizationException or IOException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static string FindRepositoryRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FluentSpecifications.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new SnippetSynchronizationException(
            "Could not locate FluentSpecifications.slnx from the current directory.");
    }

    private static IEnumerable<SourceDocument> ReadSources(string root) =>
        Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Excluded(path))
            .Order(StringComparer.Ordinal)
            .Select(path => new SourceDocument(path, File.ReadAllText(path)));

    private static bool Excluded(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj" or "node_modules" or "docs");
    }

    private static IEnumerable<string> ReadSiteSymbols(string root)
    {
        var path = Path.Combine(root, "snippets.config.json");
        if (!File.Exists(path))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("siteSymbols")
            .EnumerateArray()
            .Select(static element => element.GetString())
            .Where(static symbol => symbol is not null)
            .Select(static symbol => symbol!)
            .ToArray();
    }
}
