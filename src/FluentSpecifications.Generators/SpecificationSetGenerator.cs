using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace FluentSpecifications.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class SpecificationSetGenerator : IIncrementalGenerator
{
    private const string SpecificationSetAttribute =
        "FluentSpecifications.SpecificationSetAttribute`1";

    private const string ExposeAttribute = "FluentSpecifications.ExposeAttribute";

    private static readonly DiagnosticDescriptor InvalidCatalog = new(
        id: "FSPEC001",
        title: "Invalid specification catalog",
        messageFormat: "Specification catalog '{0}' must be a top-level, non-generic, static partial class",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/fluent-specifications/fluent-specifications");

    private static readonly DiagnosticDescriptor ExposedMemberConflict = new(
        id: "FSPEC002",
        title: "Exposed specification conflicts with an instance member",
        messageFormat: "Cannot expose rule '{0}' on '{1}' because that type already declares a member with the same name",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/fluent-specifications/fluent-specifications");

    private static readonly DiagnosticDescriptor UnsupportedLanguageVersion = new(
        id: "FSPEC003",
        title: "C# 14 is required",
        messageFormat: "Specification catalog '{0}' requires C# 14 or later for generated extension properties",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/fluent-specifications/fluent-specifications");

    private static readonly DiagnosticDescriptor UnsupportedRuleDeclaration = new(
        id: "FSPEC004",
        title: "Unsupported specification rule declaration",
        messageFormat: "Rule '{0}' is not generated: {1}",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/fluent-specifications/fluent-specifications");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var catalogs = context.SyntaxProvider.ForAttributeWithMetadataName(
            SpecificationSetAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, _) => CreateCatalog(attributeContext));

        var languageVersions = context.ParseOptionsProvider.Select(static (options, _) =>
            options is CSharpParseOptions csharp
                ? csharp.LanguageVersion
                : LanguageVersion.CSharp14);

        context.RegisterSourceOutput(catalogs.Combine(languageVersions), static (sourceContext, input) =>
        {
            var catalog = input.Left;
            if (catalog is not null)
            {
                Emit(sourceContext, catalog, input.Right);
            }
        });
    }

    private static CatalogModel? CreateCatalog(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetSymbol is not INamedTypeSymbol catalog ||
            context.Attributes.Length == 0 ||
            context.Attributes[0].AttributeClass is not { TypeArguments.Length: 1 } attribute)
        {
            return null;
        }

        var candidate = attribute.TypeArguments[0];
        var isValid = catalog.IsStatic &&
            !catalog.IsGenericType &&
            catalog.ContainingType is null &&
            IsPartial(catalog);
        var properties = ImmutableArray.CreateBuilder<RuleProperty>();
        var methods = ImmutableArray.CreateBuilder<RuleMethod>();
        var unsupported = ImmutableArray.CreateBuilder<UnsupportedRule>();

        foreach (var member in catalog.GetMembers())
        {
            if (member is IPropertySymbol property &&
                property.IsStatic &&
                property.DeclaredAccessibility == Accessibility.Public &&
                IsSpecOf(property.Type, candidate))
            {
                if (property.IsIndexer || property.SetMethod is not null)
                {
                    unsupported.Add(new UnsupportedRule(
                        property.Name,
                        "zero-argument rules must be get-only properties",
                        property.Locations.FirstOrDefault()));
                    continue;
                }

                var exposed = HasAttribute(property, ExposeAttribute);
                properties.Add(new RuleProperty(
                    property.Name,
                    exposed,
                    exposed && candidate.GetMembers(property.Name).Any(static member => !member.IsStatic),
                    property.Locations.FirstOrDefault()));
            }
            else if (member is IFieldSymbol field &&
                     field.IsStatic &&
                     field.DeclaredAccessibility == Accessibility.Public &&
                     IsSpecOf(field.Type, candidate))
            {
                if (!field.IsReadOnly)
                {
                    unsupported.Add(new UnsupportedRule(
                        field.Name,
                        "rule fields must be readonly",
                        field.Locations.FirstOrDefault()));
                    continue;
                }

                var exposed = HasAttribute(field, ExposeAttribute);
                properties.Add(new RuleProperty(
                    field.Name,
                    exposed,
                    exposed && candidate.GetMembers(field.Name).Any(static member => !member.IsStatic),
                    field.Locations.FirstOrDefault()));
            }
            else if (member is IMethodSymbol method &&
                     method.MethodKind == MethodKind.Ordinary &&
                     method.IsStatic &&
                     method.DeclaredAccessibility == Accessibility.Public &&
                     IsSpecOf(method.ReturnType, candidate))
            {
                if (method.IsGenericMethod)
                {
                    unsupported.Add(new UnsupportedRule(
                        method.Name,
                        "generic rule methods are not supported",
                        method.Locations.FirstOrDefault()));
                    continue;
                }

                if (method.Parameters.Any(static parameter =>
                        parameter.RefKind is RefKind.Ref or RefKind.Out))
                {
                    unsupported.Add(new UnsupportedRule(
                        method.Name,
                        "rule parameters cannot use ref or out",
                        method.Locations.FirstOrDefault()));
                    continue;
                }

                methods.Add(new RuleMethod(
                    method.Name,
                    method.Parameters.Select(CreateParameter).ToImmutableArray()));
            }
        }

        return new CatalogModel(
            catalog.Name,
            catalog.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
            catalog.ContainingNamespace.IsGlobalNamespace
                ? null
                : catalog.ContainingNamespace.ToDisplayString(),
            catalog.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            properties.ToImmutable(),
            methods.ToImmutable(),
            unsupported.ToImmutable(),
            isValid,
            catalog.Locations.FirstOrDefault());
    }

    private static bool IsPartial(INamedTypeSymbol catalog) =>
        catalog.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static bool IsSpecOf(ITypeSymbol type, ITypeSymbol candidate) =>
        type is INamedTypeSymbol named &&
        named.TypeArguments.Length == 1 &&
        named.OriginalDefinition.ToDisplayString() == "FluentSpecifications.Spec<T>" &&
        SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], candidate);

    private static bool HasAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static ParameterModel CreateParameter(IParameterSymbol parameter) => new(
        parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
        Escape(parameter.Name),
        parameter.RefKind switch
        {
            RefKind.Ref => "ref ",
            RefKind.Out => "out ",
            RefKind.In => "in ",
            _ => string.Empty
        },
        parameter.IsParams,
        parameter.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<ParameterSyntax>()
            .Select(static syntax => syntax.Default?.Value.ToString())
            .FirstOrDefault(static value => value is not null));

    private static string Escape(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ? $"@{identifier}" : identifier;

    private static void Emit(
        SourceProductionContext context,
        CatalogModel catalog,
        LanguageVersion languageVersion)
    {
        if (!catalog.IsValid)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidCatalog,
                catalog.Location,
                catalog.Name));
            return;
        }

        if (languageVersion < LanguageVersion.CSharp14)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedLanguageVersion,
                catalog.Location,
                catalog.Name));
            return;
        }

        foreach (var property in catalog.Properties.Where(static property => property.Conflicts))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ExposedMemberConflict,
                property.Location,
                property.Name,
                catalog.CandidateType));
        }

        foreach (var rule in catalog.UnsupportedRules)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsupportedRuleDeclaration,
                rule.Location,
                rule.Name,
                rule.Reason));
        }

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");

        if (catalog.Namespace is not null)
        {
            source.Append("namespace ").Append(catalog.Namespace).AppendLine(";");
            source.AppendLine();
        }

        source.Append(catalog.Accessibility)
            .Append(" static partial class ")
            .Append(catalog.Name)
            .AppendLine();
        source.AppendLine("{");

        foreach (var property in catalog.Properties)
        {
            source.Append("    private static readonly global::System.Lazy<global::FluentSpecifications.Spec<")
                .Append(catalog.CandidateType)
                .Append(">> ")
                .Append(CacheFieldName(property))
                .Append(" = new(() => ")
                .Append(catalog.QualifiedName)
                .Append('.')
                .Append(Escape(property.Name))
                .AppendLine(");");
        }

        if (catalog.Properties.Length > 0)
        {
            source.AppendLine();
        }

        if (catalog.Properties.Length > 0 || catalog.Methods.Length > 0)
        {
            source.Append("    extension(global::FluentSpecifications.SpecConnector<")
                .Append(catalog.CandidateType)
                .AppendLine("> connector)");
            source.AppendLine("    {");

            foreach (var property in catalog.Properties)
            {
                source.Append("        public global::FluentSpecifications.Spec<")
                    .Append(catalog.CandidateType)
                    .Append("> ")
                    .Append(Escape(property.Name))
                    .Append(" => connector(")
                    .Append(CacheFieldName(property))
                    .Append(".Value")
                    .AppendLine(");");
            }

            foreach (var method in catalog.Methods)
            {
                source.Append("        public global::FluentSpecifications.Spec<")
                    .Append(catalog.CandidateType)
                    .Append("> ")
                    .Append(Escape(method.Name))
                    .Append('(')
                    .Append(string.Join(", ", method.Parameters.Select(ParameterDeclaration)))
                    .Append(") => connector(")
                    .Append(catalog.QualifiedName)
                    .Append('.')
                    .Append(Escape(method.Name))
                    .Append('(')
                    .Append(string.Join(", ", method.Parameters.Select(ParameterArgument)))
                    .AppendLine("));");
            }

            source.AppendLine("    }");
        }

        var exposed = catalog.Properties
            .Where(static property => property.Exposed && !property.Conflicts)
            .ToArray();
        if (exposed.Length > 0)
        {
            source.AppendLine();
            source.Append("    extension(")
                .Append(catalog.CandidateType)
                .AppendLine(" candidate)");
            source.AppendLine("    {");

            foreach (var property in exposed)
            {
                source.Append("        public bool ")
                    .Append(Escape(property.Name))
                    .Append(" => ")
                    .Append(CacheFieldName(property))
                    .Append(".Value")
                    .AppendLine(".Matches(candidate);");
            }

            source.AppendLine("    }");
        }

        source.AppendLine("}");

        context.AddSource(
            $"{Sanitize(catalog.QualifiedName)}.Specifications.g.cs",
            SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static string ParameterDeclaration(ParameterModel parameter) =>
        $"{(parameter.IsParams ? "params " : parameter.RefPrefix)}{parameter.Type} {parameter.Name}" +
        (parameter.DefaultValue is null ? string.Empty : $" = {parameter.DefaultValue}");

    private static string ParameterArgument(ParameterModel parameter) =>
        $"{parameter.RefPrefix}{parameter.Name}";

    private static string CacheFieldName(RuleProperty property) =>
        $"__FluentSpecifications_Cached_{property.Name}";

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private sealed class CatalogModel
    {
        public CatalogModel(
            string name,
            string accessibility,
            string? @namespace,
            string qualifiedName,
            string candidateType,
            ImmutableArray<RuleProperty> properties,
            ImmutableArray<RuleMethod> methods,
            ImmutableArray<UnsupportedRule> unsupportedRules,
            bool isValid,
            Location? location)
        {
            Name = name;
            Accessibility = accessibility;
            Namespace = @namespace;
            QualifiedName = qualifiedName;
            CandidateType = candidateType;
            Properties = properties;
            Methods = methods;
            UnsupportedRules = unsupportedRules;
            IsValid = isValid;
            Location = location;
        }

        public string Name { get; }

        public string Accessibility { get; }

        public string? Namespace { get; }

        public string QualifiedName { get; }

        public string CandidateType { get; }

        public ImmutableArray<RuleProperty> Properties { get; }

        public ImmutableArray<RuleMethod> Methods { get; }

        public ImmutableArray<UnsupportedRule> UnsupportedRules { get; }

        public bool IsValid { get; }

        public Location? Location { get; }
    }

    private sealed class RuleProperty
    {
        public RuleProperty(string name, bool exposed, bool conflicts, Location? location)
        {
            Name = name;
            Exposed = exposed;
            Conflicts = conflicts;
            Location = location;
        }

        public string Name { get; }

        public bool Exposed { get; }

        public bool Conflicts { get; }

        public Location? Location { get; }
    }

    private sealed class RuleMethod
    {
        public RuleMethod(string name, ImmutableArray<ParameterModel> parameters)
        {
            Name = name;
            Parameters = parameters;
        }

        public string Name { get; }

        public ImmutableArray<ParameterModel> Parameters { get; }
    }

    private sealed class ParameterModel
    {
        public ParameterModel(
            string type,
            string name,
            string refPrefix,
            bool isParams,
            string? defaultValue)
        {
            Type = type;
            Name = name;
            RefPrefix = refPrefix;
            IsParams = isParams;
            DefaultValue = defaultValue;
        }

        public string Type { get; }

        public string Name { get; }

        public string RefPrefix { get; }

        public bool IsParams { get; }

        public string? DefaultValue { get; }
    }

    private sealed class UnsupportedRule
    {
        public UnsupportedRule(string name, string reason, Location? location)
        {
            Name = name;
            Reason = reason;
            Location = location;
        }

        public string Name { get; }

        public string Reason { get; }

        public Location? Location { get; }
    }
}
