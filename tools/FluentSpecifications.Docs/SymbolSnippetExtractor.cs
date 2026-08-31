using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace FluentSpecifications.Docs;

public sealed record SourceDocument(string Path, string Content);

public sealed class SymbolSnippetExtractor
{
    public IReadOnlyDictionary<string, string> Extract(
        IEnumerable<SourceDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var sourceDocuments = documents.ToArray();
        var syntaxTrees = sourceDocuments
            .Select(document => CSharpSyntaxTree.ParseText(
                document.Content,
                new CSharpParseOptions(LanguageVersion.CSharp14),
                document.Path))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "FluentSpecifications.Documentation",
            [.. syntaxTrees, ImplicitUsingsTree],
            PlatformReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var snippets = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var syntaxTree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();

            foreach (var declaration in Declarations(root))
            {
                var symbol = semanticModel.GetDeclaredSymbol(declaration);
                var documentationId = symbol?.GetDocumentationCommentId();
                if (documentationId is null)
                {
                    continue;
                }

                var snippetNode = SnippetNode(declaration);
                var snippet = Dedent(snippetNode.ToFullString());
                if (!snippets.TryAdd(documentationId, snippet) &&
                    !string.Equals(snippets[documentationId], snippet, StringComparison.Ordinal))
                {
                    throw new SnippetSynchronizationException(
                        $"Symbol '{documentationId}' has multiple source declarations.");
                }
            }
        }

        return snippets;
    }

    private static IEnumerable<SyntaxNode> Declarations(SyntaxNode root) =>
        root.DescendantNodes().Where(static node =>
            node is BaseTypeDeclarationSyntax or
                DelegateDeclarationSyntax or
                MethodDeclarationSyntax or
                PropertyDeclarationSyntax or
                EventDeclarationSyntax or
                EnumMemberDeclarationSyntax or
                VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax });

    private static SyntaxNode SnippetNode(SyntaxNode declaration) =>
        declaration is VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field }
            ? field
            : declaration;

    private static string Dedent(string source)
    {
        var lines = source.ReplaceLineEndings("\n")
            .Trim('\n', '\r')
            .Split('\n');
        var indentation = lines
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line.TakeWhile(char.IsWhiteSpace).Count())
            .DefaultIfEmpty(0)
            .Min();

        return string.Join(
                "\n",
                lines.Select(line => line.Length >= indentation
                    ? line[indentation..]
                    : string.Empty))
            .TrimEnd();
    }

    private static IReadOnlyList<MetadataReference> PlatformReferences { get; } =
        ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
        ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(static path => MetadataReference.CreateFromFile(path))
        .ToArray() ?? [];

    private static SyntaxTree ImplicitUsingsTree { get; } =
        CSharpSyntaxTree.ParseText(
            """
            global using System;
            global using System.Collections.Generic;
            global using System.IO;
            global using System.Linq;
            global using System.Net.Http;
            global using System.Threading;
            global using System.Threading.Tasks;
            """,
            new CSharpParseOptions(LanguageVersion.CSharp14),
            "DocumentationImplicitUsings.g.cs");
}
