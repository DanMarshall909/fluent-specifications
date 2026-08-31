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

    private static GeneratorDriverRunResult Run(
        string source,
        LanguageVersion languageVersion = LanguageVersion.CSharp14)
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(static path => MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(Spec<>).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SpecificationSetGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);

        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
