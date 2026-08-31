using FluentSpecifications.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FluentSpecifications.Generators.Tests;

public sealed class GeneratorDiagnosticTests
{
    [Fact]
    public void Catalog_must_be_a_top_level_non_generic_static_partial_class()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>]
            public class OrderRules
            {
                public static Spec<Order> Paid =>
                    Spec.Define<Order>("order.paid", "Paid", _ => true);
            }
            """;

        var result = Run(source);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "FSPEC001");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Exposed_property_must_not_hide_an_instance_member()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order
            {
                public bool CanShip => false;
            }

            [SpecificationSet<Order>]
            public static partial class OrderRules
            {
                [Expose]
                public static Spec<Order> CanShip =>
                    Spec.Define<Order>("order.can-ship", "Can ship", _ => true);
            }
            """;

        var result = Run(source);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "FSPEC002");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void Extension_properties_require_csharp_14()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>]
            public static partial class OrderRules
            {
                public static Spec<Order> Paid =>
                    Spec.Define<Order>("order.paid", "Paid", _ => true);
            }
            """;

        var result = Run(source, LanguageVersion.CSharp13);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "FSPEC003");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Unsupported_rule_shapes_are_reported_instead_of_silently_disappearing()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>]
            public static partial class OrderRules
            {
                public static Spec<Order> Mutable =
                    Spec.Define<Order>("order.mutable", "Mutable", _ => true);

                public static Spec<Order> Settable { get; set; } =
                    Spec.Define<Order>("order.settable", "Settable", _ => true);

                public static Spec<Order> Generic<T>(T value) =>
                    Spec.Define<Order>("order.generic", "Generic", _ => value != null);

                public static Spec<Order> WithOutput(out int value)
                {
                    value = 1;
                    return Spec.Define<Order>("order.output", "Output", _ => true);
                }
            }
            """;

        var result = Run(source);

        var diagnostics = result.Diagnostics
            .Where(item => item.Id == "FSPEC004")
            .ToArray();
        Assert.Equal(4, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void Existing_catalogs_do_not_generate_search_until_one_opts_in()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order
            {
                public static object Search => new();
            }

            [SpecificationSet<Order>]
            public static partial class FulfilmentRules
            {
                public static Spec<Order> Ready =>
                    Spec.Define<Order>("order.ready", "Ready", _ => true);
            }

            [SpecificationSet<Order>]
            public static partial class RiskRules
            {
                public static Spec<Order> Safe =>
                    Spec.Define<Order>("order.safe", "Safe", _ => true);
            }
            """;

        var result = Run(source);

        Assert.DoesNotContain(
            result.Diagnostics,
            item => item.Id is "FSPEC005" or "FSPEC006");
        Assert.All(
            result.GeneratedTrees,
            tree => Assert.DoesNotContain("SearchRoot", tree.GetText().ToString()));
    }

    [Fact]
    public void One_entity_cannot_infer_search_language_from_multiple_primary_catalogs()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class FulfilmentRules
            {
                public static Spec<Order> Ready =>
                    Spec.Define<Order>("order.ready", "Ready", _ => true);
            }

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class RiskRules
            {
                public static Spec<Order> Safe =>
                    Spec.Define<Order>("order.safe", "Safe", _ => true);
            }
            """;

        var result = Run(source);

        Assert.Equal(2, result.Diagnostics.Count(item => item.Id == "FSPEC005"));
    }

    [Fact]
    public void Secondary_catalog_can_opt_out_of_inferred_search_language()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class FulfilmentRules
            {
                public static Spec<Order> Ready =>
                    Spec.Define<Order>("order.ready", "Ready", _ => true);
            }

            [SpecificationSet<Order>(GenerateSearch = false)]
            public static partial class RiskRules
            {
                public static Spec<Order> Safe =>
                    Spec.Define<Order>("order.safe", "Safe", _ => true);
            }
            """;

        var result = Run(source);

        Assert.DoesNotContain(result.Diagnostics, item => item.Id == "FSPEC005");
    }

    [Fact]
    public void Entity_members_must_not_silently_hide_generated_search_entry_points()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order
            {
                public static object Search => new();
            }

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public static Spec<Order> Ready =>
                    Spec.Define<Order>("order.ready", "Ready", _ => true);
            }
            """;

        var result = Run(source);

        var diagnostic = Assert.Single(result.Diagnostics, item => item.Id == "FSPEC006");
        Assert.Contains("Search", diagnostic.GetMessage());
    }

    [Fact]
    public void Search_support_types_do_not_replace_existing_catalog_members()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public sealed class SearchRoot;

                public sealed class RuleCatalog;

                public static object SearchRuleCatalog => new();

                public static void FieldCatalog()
                {
                }

                public static Spec<Order> Ready =>
                    Spec.Define<Order>("order.ready", "Ready", _ => true);
            }
            """;

        var result = Run(source, treatWarningsAsErrors: true);

        var diagnostics = result.Diagnostics
            .Where(item => item.Id == "FSPEC007")
            .ToArray();
        Assert.Equal(4, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        Assert.All(
            result.GeneratedTrees,
            tree => Assert.DoesNotContain(
                "public readonly struct SearchRoot",
                tree.GetText().ToString()));
    }

    [Fact]
    public void Search_fields_use_the_effective_most_derived_member()
    {
        const string source = """
            using FluentSpecifications;

            public abstract class Entity
            {
                public virtual int Id => 0;

                public string Reference => "base";
            }

            public sealed class Order : Entity
            {
                public override int Id => 1;

                public new string Reference => "derived";
            }

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public static Spec<Order> Ready =>
                    Spec.Define<Order>("order.ready", "Ready", _ => true);
            }
            """;

        _ = Run(source, treatWarningsAsErrors: true);
    }

    [Fact]
    public void Search_fields_preserve_nested_nullable_annotations()
    {
        const string source = """
            using System.Collections.Generic;
            using FluentSpecifications;

            public sealed class Order
            {
                public IReadOnlyList<string?> Tags { get; } = [];
            }

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public static Spec<Order> Ready =>
                    Spec.Define<Order>("order.ready", "Ready", _ => true);
            }
            """;

        var result = Run(source, treatWarningsAsErrors: true);

        Assert.Contains(
            "global::System.Collections.Generic.IReadOnlyList<string?>",
            Assert.Single(result.GeneratedTrees).GetText().ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_named_rule_coexists_with_the_dynamic_search_escape_hatch()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public static Spec<Order> Rule =>
                    Spec.Define<Order>("order.rule", "Rule", _ => true);
            }

            public static class Searches
            {
                public static UnsortedSearch<Order> Named =>
                    Order.Search.Matching.Rule;

                public static UnsortedSearch<Order> Dynamic(Spec<Order> specification) =>
                    Order.Search.For(specification);
            }
            """;

        _ = Run(source, treatWarningsAsErrors: true);
    }

    [Fact]
    public void Object_member_field_names_remain_available_through_dynamic_indexers()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order
            {
                public new int Equals => 1;

                public new int GetHashCode => 2;

                public new int GetType => 3;

                public new int MemberwiseClone => 4;

                public new int ReferenceEquals => 5;

                public new int ToString => 6;
            }

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public static Spec<Order> Ready =>
                    Spec.Define<Order>("order.ready", "Ready", _ => true);
            }

            public static class Searches
            {
                public static OrderedSearch<Order> Dynamic => Order.Search.All
                    .Sorted.By[Order.Fields.Equals].Asc
                    .Then.By[Order.Fields.GetHashCode].Desc
                    .Then.By[Order.Fields.GetType].Asc
                    .Then.By[Order.Fields.MemberwiseClone].Desc
                    .Then.By[Order.Fields.ReferenceEquals].Asc
                    .Then.By[Order.Fields.ToString].Desc;
            }
            """;

        _ = Run(source, treatWarningsAsErrors: true);
    }

    [Fact]
    public void Object_member_rule_names_compile_in_generated_catalogs()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public new static Spec<Order> Equals => Rule("Equals");

                public static Spec<Order> Finalize => Rule("Finalize");

                public new static Spec<Order> GetHashCode => Rule("GetHashCode");

                public new static Spec<Order> GetType => Rule("GetType");

                public new static Spec<Order> MemberwiseClone => Rule("MemberwiseClone");

                public new static Spec<Order> ReferenceEquals => Rule("ReferenceEquals");

                public new static Spec<Order> ToString => Rule("ToString");

                private static Spec<Order> Rule(string name) =>
                    Spec.Define<Order>($"order.{name}", name, _ => true);
            }

            public static class Searches
            {
                public static UnsortedSearch<Order> Named =>
                    Order.Search.Matching.ReferenceEquals;

                public static UnsortedSearch<Order> Explicit =>
                    Order.Search.For(Order.Rules.MemberwiseClone);
            }
            """;

        _ = Run(source, treatWarningsAsErrors: true);
    }

    [Fact]
    public void Only_matching_object_rule_method_signatures_use_explicit_hiding()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public new static Spec<Order> Equals(object value) => Rule("Equals-one");

                public new static Spec<Order> Equals(object left, object right) => Rule("Equals-two");

                public new static Spec<Order> GetHashCode() => Rule("GetHashCode");

                public new static Spec<Order> GetType() => Rule("GetType");

                public new static Spec<Order> MemberwiseClone() => Rule("MemberwiseClone");

                public new static Spec<Order> ReferenceEquals(object left, object right) =>
                    Rule("ReferenceEquals");

                public new static Spec<Order> ToString() => Rule("ToString");

                public static Spec<Order> ToString(string format) => Rule(format);

                private static Spec<Order> Rule(string name) =>
                    Spec.Define<Order>($"order.{name}", name, _ => true);
            }
            """;

        _ = Run(source, treatWarningsAsErrors: true);
    }

    [Fact]
    public void Dynamic_rule_parameters_use_their_object_equivalent_signature()
    {
        const string source = """
            using FluentSpecifications;

            public sealed class Order;

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public new static Spec<Order> Equals(dynamic value) => Rule("Equals");

                public new static Spec<Order> ReferenceEquals(dynamic left, dynamic right) =>
                    Rule("ReferenceEquals");

                private static Spec<Order> Rule(string name) =>
                    Spec.Define<Order>($"order.{name}", name, _ => true);
            }
            """;

        _ = Run(source, treatWarningsAsErrors: true);
    }

    private static GeneratorDriverRunResult Run(
        string source,
        LanguageVersion languageVersion = LanguageVersion.CSharp14,
        bool treatWarningsAsErrors = false)
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Spec<>).Assembly.Location));

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            generalDiagnosticOption: treatWarningsAsErrors
                ? ReportDiagnostic.Error
                : ReportDiagnostic.Default);
        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [syntaxTree],
            references,
            compilationOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SpecificationSetGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out _);
        var compilerErrors = outputCompilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Id.StartsWith("CS", StringComparison.Ordinal))
            .ToArray();
        Assert.True(
            compilerErrors.Length == 0,
            string.Join(Environment.NewLine, compilerErrors.Select(static item => item.ToString())));

        return driver.GetRunResult();
    }
}
