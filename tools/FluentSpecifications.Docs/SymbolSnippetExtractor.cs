using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace FluentSpecifications.Docs;

public sealed record SourceDocument(string Path, string Content);

public sealed record ParameterHint(int Offset, string Name);

public sealed record ExtractedSnippet(
    string Code,
    IReadOnlyList<ParameterHint> ParameterHints);

public sealed class SymbolSnippetExtractor
{
    public IReadOnlyDictionary<string, string> Extract(
        IEnumerable<SourceDocument> documents) =>
        ExtractDetailed(documents).ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Code,
            StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ExtractedSnippet> ExtractDetailed(
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
        var snippets = new Dictionary<string, ExtractedSnippet>(StringComparer.Ordinal);

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

                AddSnippet(
                    snippets,
                    documentationId,
                    semanticModel,
                    SnippetNode(declaration));

                if (declaration is MethodDeclarationSyntax method)
                {
                    var locals = method.DescendantNodes()
                        .OfType<LocalDeclarationStatementSyntax>()
                        .Where(static local => local.Declaration.Variables.Count == 1)
                        .GroupBy(
                            static local => local.Declaration.Variables[0].Identifier.ValueText,
                            StringComparer.Ordinal)
                        .Where(static group => group.Count() == 1)
                        .Select(static group => group.Single());
                    foreach (var local in locals)
                    {
                        var localName = local.Declaration.Variables[0].Identifier.ValueText;
                        AddSnippet(
                            snippets,
                            $"{documentationId}|local:{localName}",
                            semanticModel,
                            local);
                    }
                }
            }
        }

        return snippets;
    }

    private static void AddSnippet(
        IDictionary<string, ExtractedSnippet> snippets,
        string symbolicName,
        SemanticModel semanticModel,
        SyntaxNode snippetNode)
    {
        var snippetText = Dedent(snippetNode.ToFullString());
        var snippet = new ExtractedSnippet(
            snippetText.Code,
            ParameterHints(semanticModel, snippetNode, snippetText));
        if (!snippets.TryAdd(symbolicName, snippet) &&
            (!string.Equals(
                snippets[symbolicName].Code,
                snippet.Code,
                StringComparison.Ordinal) ||
            !snippets[symbolicName].ParameterHints.SequenceEqual(
                snippet.ParameterHints)))
        {
            throw new SnippetSynchronizationException(
                $"Symbolic snippet '{symbolicName}' has multiple source declarations.");
        }
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

    private static IReadOnlyList<ParameterHint> ParameterHints(
        SemanticModel semanticModel,
        SyntaxNode snippetNode,
        DedentedSnippet snippet)
    {
        var seenParameters = new Dictionary<SyntaxNode, HashSet<IParameterSymbol>>();
        var hints = new List<ParameterHint>();

        foreach (var argument in snippetNode.DescendantNodes()
            .OfType<ArgumentSyntax>()
            .OrderBy(static argument => argument.SpanStart))
        {
            if (argument.NameColon is not null ||
                ResolveParameter(semanticModel, argument) is not { } parameter ||
                IsSelfExplanatory(argument.Expression, parameter))
            {
                continue;
            }

            var argumentList = argument.Parent!;
            if (!seenParameters.TryGetValue(argumentList, out var parameters))
            {
                parameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
                seenParameters.Add(argumentList, parameters);
            }

            if (!parameters.Add(parameter))
            {
                continue;
            }

            var relativeOffset = NormalizedLength(
                snippetNode.SyntaxTree.GetText().ToString(TextSpan.FromBounds(
                    snippetNode.FullSpan.Start,
                    argument.Expression.SpanStart)));
            hints.Add(new ParameterHint(
                snippet.ToOutputOffset(relativeOffset),
                parameter.Name));
        }

        return hints;
    }

    private static IParameterSymbol? ResolveParameter(
        SemanticModel semanticModel,
        ArgumentSyntax argument)
    {
        if (semanticModel.GetOperation(argument) is IArgumentOperation
            {
                Parameter: { } parameter
            })
        {
            return parameter;
        }

        if (argument.Parent is not BaseArgumentListSyntax argumentList ||
            argumentList.Parent is not InvocationExpressionSyntax invocation)
        {
            return null;
        }

        var index = argumentList.Arguments.IndexOf(argument);
        if (semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
        {
            return ParameterAt(method, index);
        }

        var methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };
        if (methodName is null)
        {
            return null;
        }

        var candidates = semanticModel.Compilation
            .GetSymbolsWithName(methodName, SymbolFilter.Member)
            .OfType<IMethodSymbol>()
            .Select(candidate => ParameterAt(candidate, index))
            .Where(static parameter => parameter is not null)
            .Cast<IParameterSymbol>()
            .GroupBy(static parameter => parameter.Name, StringComparer.Ordinal)
            .ToArray();
        return candidates.Length == 1
            ? candidates[0].First()
            : null;
    }

    private static IParameterSymbol? ParameterAt(IMethodSymbol method, int index)
    {
        if (index < method.Parameters.Length)
        {
            return method.Parameters[index];
        }

        return method.Parameters.LastOrDefault(static parameter => parameter.IsParams);
    }

    private static bool IsSelfExplanatory(
        ExpressionSyntax expression,
        IParameterSymbol parameter) =>
        expression is IdentifierNameSyntax identifier &&
        string.Equals(
            identifier.Identifier.ValueText,
            parameter.Name,
            StringComparison.OrdinalIgnoreCase);

    private static DedentedSnippet Dedent(string source)
    {
        var normalized = source.ReplaceLineEndings("\n");
        var leadingTrim = normalized.Length - normalized.TrimStart('\n').Length;
        var trimmed = normalized.Trim('\n');
        var lines = trimmed.Split('\n');
        var indentation = lines
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line.TakeWhile(char.IsWhiteSpace).Count())
            .DefaultIfEmpty(0)
            .Min();

        var code = string.Join(
                "\n",
                lines.Select(line => line.Length >= indentation
                    ? line[indentation..]
                    : string.Empty))
            .TrimEnd();
        return new DedentedSnippet(code, trimmed, leadingTrim, indentation);
    }

    private static int NormalizedLength(string source) =>
        source.ReplaceLineEndings("\n").Length;

    private sealed record DedentedSnippet(
        string Code,
        string TrimmedSource,
        int LeadingTrim,
        int Indentation)
    {
        public int ToOutputOffset(int normalizedSourceOffset)
        {
            var trimmedOffset = normalizedSourceOffset - LeadingTrim;
            var preceding = TrimmedSource[..trimmedOffset];
            var lines = preceding.Split('\n');
            var offset = 0;

            foreach (var line in lines[..^1])
            {
                offset += Math.Max(0, line.Length - Indentation) + 1;
            }

            return offset + Math.Max(0, lines[^1].Length - Indentation);
        }
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
