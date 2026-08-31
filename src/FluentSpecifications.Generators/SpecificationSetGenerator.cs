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

    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor InvalidCatalog = new(
        id: "FSPEC001",
        title: "Invalid specification catalog",
        messageFormat: "Specification catalog '{0}' must be a top-level, non-generic, static partial class",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/DanMarshall909/fluent-specifications");

    private static readonly DiagnosticDescriptor ExposedMemberConflict = new(
        id: "FSPEC002",
        title: "Exposed specification conflicts with an instance member",
        messageFormat: "Cannot expose rule '{0}' on '{1}' because that type already declares a member with the same name",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/DanMarshall909/fluent-specifications");

    private static readonly DiagnosticDescriptor UnsupportedLanguageVersion = new(
        id: "FSPEC003",
        title: "C# 14 is required",
        messageFormat: "Specification catalog '{0}' requires C# 14 or later for generated extension properties",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/DanMarshall909/fluent-specifications");

    private static readonly DiagnosticDescriptor UnsupportedRuleDeclaration = new(
        id: "FSPEC004",
        title: "Unsupported specification rule declaration",
        messageFormat: "Rule '{0}' is not generated: {1}",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/DanMarshall909/fluent-specifications");

    private static readonly DiagnosticDescriptor MultipleSearchCatalogs = new(
        id: "FSPEC005",
        title: "Multiple inferred search catalogs",
        messageFormat: "Candidate type '{0}' has multiple specification catalogs generating Order.Search; set GenerateSearch = false on all but one catalog",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/DanMarshall909/fluent-specifications");

    private static readonly DiagnosticDescriptor SearchEntryPointConflict = new(
        id: "FSPEC006",
        title: "Generated search entry point conflicts with an entity member",
        messageFormat: "Cannot generate '{0}.{1}' because the candidate type already declares a member named '{1}'",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/DanMarshall909/fluent-specifications");

    private static readonly DiagnosticDescriptor SearchSupportMemberConflict = new(
        id: "FSPEC007",
        title: "Generated search support conflicts with a catalog member",
        messageFormat: "Cannot generate search language for specification catalog '{0}' because it already declares a member named '{1}', which is reserved for generated search support",
        category: "FluentSpecifications.Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/DanMarshall909/fluent-specifications");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var catalogs = context.SyntaxProvider.ForAttributeWithMetadataName(
            SpecificationSetAttribute,
            static (node, _) => node is ClassDeclarationSyntax,
            static (attributeContext, _) => CreateCatalog(attributeContext))
            .Where(static catalog => catalog is not null)
            .Select(static (catalog, _) => catalog!);

        var languageVersions = context.ParseOptionsProvider.Select(static (options, _) =>
            options is CSharpParseOptions csharp
                ? csharp.LanguageVersion
                : LanguageVersion.CSharp14);

        context.RegisterSourceOutput(catalogs.Collect().Combine(languageVersions), static (sourceContext, input) =>
        {
            var duplicateCandidates = input.Left
                .Where(static catalog => catalog.GenerateSearch)
                .GroupBy(static catalog => catalog.CandidateType, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .Select(static group => group.Key)
                .ToImmutableHashSet(StringComparer.Ordinal);

            foreach (var catalog in input.Left)
            {
                var duplicate = duplicateCandidates.Contains(catalog.CandidateType);
                if (duplicate && catalog.GenerateSearch)
                {
                    sourceContext.ReportDiagnostic(Diagnostic.Create(
                        MultipleSearchCatalogs,
                        catalog.Location,
                        catalog.CandidateType));
                }

                Emit(
                    sourceContext,
                    catalog,
                    input.Right,
                    emitSearch: catalog.GenerateSearch &&
                        !duplicate &&
                        catalog.SearchConflicts.Length == 0 &&
                        catalog.SearchSupportConflicts.Length == 0);
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
        var generateSearch = context.Attributes[0].NamedArguments.Any(static argument =>
            argument.Key == "GenerateSearch" && argument.Value.Value is true);
        var isValid = catalog.IsStatic &&
            !catalog.IsGenericType &&
            catalog.ContainingType is null &&
            IsPartial(catalog);
        var properties = ImmutableArray.CreateBuilder<RuleProperty>();
        var methods = ImmutableArray.CreateBuilder<RuleMethod>();
        var unsupported = ImmutableArray.CreateBuilder<UnsupportedRule>();
        var fields = ImmutableArray.CreateBuilder<FieldModel>();

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

        foreach (var member in CandidateMembers(candidate))
        {
            if (member is IPropertySymbol property &&
                !property.IsStatic &&
                !property.IsIndexer &&
                property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
            {
                fields.Add(new FieldModel(
                    property.Name,
                    DisplayFieldType(property.Type)));
            }
            else if (member is IFieldSymbol field &&
                     !field.IsStatic &&
                     field.DeclaredAccessibility == Accessibility.Public)
            {
                fields.Add(new FieldModel(
                    field.Name,
                    DisplayFieldType(field.Type)));
            }
        }


        var searchConflicts = generateSearch
            ? CandidateMembers(candidate)
                .Where(static member => member.Name is "Search" or "Rules" or "Fields")
                .Select(static member => member.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;

        var searchSupportConflicts = generateSearch
            ? catalog.GetMembers()
                .Where(static member => member.Name is
                    "SearchRoot" or
                    "RuleCatalog" or
                    "SearchRuleCatalog" or
                    "FieldCatalog")
                .Select(static member => member.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;

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
            fields.ToImmutable(),
            unsupported.ToImmutable(),
            generateSearch,
            searchConflicts,
            searchSupportConflicts,
            isValid,
            catalog.Locations.FirstOrDefault());
    }

    private static bool IsPartial(INamedTypeSymbol catalog) =>
        catalog.DeclaringSyntaxReferences
            .Select(static reference => reference.GetSyntax())
            .OfType<ClassDeclarationSyntax>()
            .Any(static declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static IEnumerable<ISymbol> CandidateMembers(ITypeSymbol candidate)
    {
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        for (var current = candidate as INamedTypeSymbol;
             current is not null;
             current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (seenNames.Add(member.Name))
                {
                    yield return member;
                }
            }
        }
    }

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
        parameter.Type.SpecialType == SpecialType.System_Object ||
            parameter.Type.TypeKind == TypeKind.Dynamic,
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

    private static string DisplayFieldType(ITypeSymbol type)
        => type.ToDisplayString(FullyQualifiedNullableFormat);

    private static void Emit(
        SourceProductionContext context,
        CatalogModel catalog,
        LanguageVersion languageVersion,
        bool emitSearch)
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


        foreach (var conflict in catalog.SearchConflicts)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                SearchEntryPointConflict,
                catalog.Location,
                catalog.CandidateType,
                conflict));
        }

        foreach (var conflict in catalog.SearchSupportConflicts)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                SearchSupportMemberConflict,
                catalog.Location,
                catalog.Name,
                conflict));
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

        if (emitSearch)
        {
            foreach (var field in catalog.Fields)
            {
                source.Append("    private static readonly global::FluentSpecifications.SearchField<")
                    .Append(catalog.CandidateType)
                    .Append("> ")
                    .Append(CacheFieldName(field))
                    .Append(" = global::FluentSpecifications.SearchField.Define<")
                    .Append(catalog.CandidateType)
                    .Append(", ")
                    .Append(field.Type)
                    .Append(">(")
                    .Append(SymbolDisplay.FormatLiteral(field.Name, quote: true))
                    .Append(", candidate => candidate.")
                    .Append(Escape(field.Name))
                    .AppendLine(");");
            }
        }

        if (catalog.Properties.Length > 0 || (emitSearch && catalog.Fields.Length > 0))
        {
            source.AppendLine();
        }

        if (emitSearch)
        {
            EmitCatalogTypes(source, catalog);
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

            if (emitSearch)
            {
                source.AppendLine();
                source.Append("    extension(global::FluentSpecifications.SearchRuleConnector<")
                    .Append(catalog.CandidateType)
                    .AppendLine("> connector)");
                source.AppendLine("    {");

                foreach (var property in catalog.Properties)
                {
                    source.Append("        public global::FluentSpecifications.UnsortedSearch<")
                        .Append(catalog.CandidateType)
                        .Append("> ")
                        .Append(Escape(property.Name))
                        .Append(" => connector(")
                        .Append(CacheFieldName(property))
                        .AppendLine(".Value);");
                }

                foreach (var method in catalog.Methods)
                {
                    source.Append("        public global::FluentSpecifications.UnsortedSearch<")
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
        }

        if (emitSearch)
        {
            EmitFieldSelectorExtensions(source, catalog);
        }

        var exposed = catalog.Properties
            .Where(static property => property.Exposed && !property.Conflicts)
            .ToArray();

        if (emitSearch || exposed.Length > 0)
        {
            source.AppendLine();
            source.Append("    extension(")
                .Append(catalog.CandidateType)
                .AppendLine(" candidate)");
            source.AppendLine("    {");
            if (emitSearch)
            {
                source.AppendLine("        public static SearchRoot Search => default;");
                source.AppendLine("        public static RuleCatalog Rules => default;");
                source.AppendLine("        public static FieldCatalog Fields => default;");
            }

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

    private static string CacheFieldName(FieldModel field) =>
        $"__FluentSpecifications_Field_{Sanitize(field.Name)}";

    private static void EmitCatalogTypes(StringBuilder source, CatalogModel catalog)
    {
        source.AppendLine("    public readonly struct SearchRoot");
        source.AppendLine("    {");
        source.AppendLine("        public SearchRuleCatalog Matching => default;");
        source.Append("        public global::FluentSpecifications.UnsortedSearch<")
            .Append(catalog.CandidateType)
            .Append("> For(global::FluentSpecifications.Spec<")
            .Append(catalog.CandidateType)
            .Append("> specification) => global::FluentSpecifications.Search.Matching(specification);")
            .AppendLine();
        source.Append("        public global::FluentSpecifications.UnsortedSearch<")
            .Append(catalog.CandidateType)
            .Append("> All => global::FluentSpecifications.Search.All<")
            .Append(catalog.CandidateType)
            .AppendLine(">();");
        source.AppendLine("    }");
        source.AppendLine();

        source.AppendLine("    public readonly struct RuleCatalog");
        source.AppendLine("    {");
        EmitRuleCatalogMembers(source, catalog, "global::FluentSpecifications.Spec", false);
        source.AppendLine("    }");
        source.AppendLine();

        source.AppendLine("    public readonly struct SearchRuleCatalog");
        source.AppendLine("    {");
        EmitRuleCatalogMembers(
            source,
            catalog,
            "global::FluentSpecifications.UnsortedSearch",
            true);
        source.AppendLine("    }");
        source.AppendLine();

        source.AppendLine("    public readonly struct FieldCatalog");
        source.AppendLine("    {");
        foreach (var field in catalog.Fields)
        {
            source.Append(HidesObjectProperty(field.Name)
                    ? "        public new global::FluentSpecifications.SearchField<"
                    : "        public global::FluentSpecifications.SearchField<")
                .Append(catalog.CandidateType)
                .Append("> ")
                .Append(Escape(field.Name))
                .Append(" => ")
                .Append(CacheFieldName(field))
                .AppendLine(";");
        }

        source.AppendLine("    }");
        source.AppendLine();
    }

    private static bool HidesObjectProperty(string name) =>
        name is "Equals" or
            "GetHashCode" or
            "GetType" or
            "MemberwiseClone" or
            "ReferenceEquals" or
            "ToString";

    private static bool HidesObjectMethod(RuleMethod method)
    {
        if (method.Parameters.Any(static parameter => parameter.RefPrefix.Length > 0))
        {
            return false;
        }

        return method.Name switch
        {
            "Equals" => HasObjectParameters(method, 1) || HasObjectParameters(method, 2),
            "ReferenceEquals" => HasObjectParameters(method, 2),
            "GetHashCode" or "GetType" or "MemberwiseClone" or "ToString" =>
                method.Parameters.Length == 0,
            _ => false
        };
    }

    private static bool HasObjectParameters(RuleMethod method, int count) =>
        method.Parameters.Length == count &&
        method.Parameters.All(static parameter => parameter.IsObjectSignatureType);

    private static void EmitRuleCatalogMembers(
        StringBuilder source,
        CatalogModel catalog,
        string resultType,
        bool createsSearch)
    {
        foreach (var property in catalog.Properties)
        {
            source.Append(HidesObjectProperty(property.Name)
                    ? "        public new "
                    : "        public ")
                .Append(resultType)
                .Append('<')
                .Append(catalog.CandidateType)
                .Append("> ")
                .Append(Escape(property.Name))
                .Append(" => ");
            if (createsSearch)
            {
                source.Append("global::FluentSpecifications.Search.Matching(");
            }

            source.Append(CacheFieldName(property)).Append(".Value");
            if (createsSearch)
            {
                source.Append(')');
            }

            source.AppendLine(";");
        }

        foreach (var method in catalog.Methods)
        {
            source.Append(HidesObjectMethod(method)
                    ? "        public new "
                    : "        public ")
                .Append(resultType)
                .Append('<')
                .Append(catalog.CandidateType)
                .Append("> ")
                .Append(Escape(method.Name))
                .Append('(')
                .Append(string.Join(", ", method.Parameters.Select(ParameterDeclaration)))
                .Append(") => ");
            if (createsSearch)
            {
                source.Append("global::FluentSpecifications.Search.Matching(");
            }

            source.Append(catalog.QualifiedName)
                .Append('.')
                .Append(Escape(method.Name))
                .Append('(')
                .Append(string.Join(", ", method.Parameters.Select(ParameterArgument)))
                .Append(')');
            if (createsSearch)
            {
                source.Append(')');
            }

            source.AppendLine(";");
        }
    }

    private static void EmitFieldSelectorExtensions(StringBuilder source, CatalogModel catalog)
    {
        if (catalog.Fields.Length == 0)
        {
            return;
        }

        source.AppendLine();
        source.Append("    extension(global::FluentSpecifications.PrimaryFieldSelector<")
            .Append(catalog.CandidateType)
            .AppendLine("> selector)");
        source.AppendLine("    {");
        foreach (var field in catalog.Fields)
        {
            source.Append("        public global::FluentSpecifications.PrimaryDirectionSelector<")
                .Append(catalog.CandidateType)
                .Append("> ")
                .Append(Escape(field.Name))
                .Append(" => selector[")
                .Append(CacheFieldName(field))
                .AppendLine("];");
        }

        source.AppendLine("    }");
        source.AppendLine();
        source.Append("    extension(global::FluentSpecifications.SecondaryFieldSelector<")
            .Append(catalog.CandidateType)
            .AppendLine("> selector)");
        source.AppendLine("    {");
        foreach (var field in catalog.Fields)
        {
            source.Append("        public global::FluentSpecifications.SecondaryDirectionSelector<")
                .Append(catalog.CandidateType)
                .Append("> ")
                .Append(Escape(field.Name))
                .Append(" => selector[")
                .Append(CacheFieldName(field))
                .AppendLine("];");
        }

        source.AppendLine("    }");
    }

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
            ImmutableArray<FieldModel> fields,
            ImmutableArray<UnsupportedRule> unsupportedRules,
            bool generateSearch,
            ImmutableArray<string> searchConflicts,
            ImmutableArray<string> searchSupportConflicts,
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
            Fields = fields;
            UnsupportedRules = unsupportedRules;
            GenerateSearch = generateSearch;
            SearchConflicts = searchConflicts;
            SearchSupportConflicts = searchSupportConflicts;
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

        public ImmutableArray<FieldModel> Fields { get; }

        public ImmutableArray<UnsupportedRule> UnsupportedRules { get; }

        public bool GenerateSearch { get; }

        public ImmutableArray<string> SearchConflicts { get; }

        public ImmutableArray<string> SearchSupportConflicts { get; }

        public bool IsValid { get; }

        public Location? Location { get; }
    }

    private sealed class FieldModel
    {
        public FieldModel(string name, string type)
        {
            Name = name;
            Type = type;
        }

        public string Name { get; }

        public string Type { get; }
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
            bool isObjectSignatureType,
            string name,
            string refPrefix,
            bool isParams,
            string? defaultValue)
        {
            Type = type;
            IsObjectSignatureType = isObjectSignatureType;
            Name = name;
            RefPrefix = refPrefix;
            IsParams = isParams;
            DefaultValue = defaultValue;
        }

        public string Type { get; }

        public bool IsObjectSignatureType { get; }

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
