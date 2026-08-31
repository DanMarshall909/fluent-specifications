using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using System.Xml.Linq;
using Xunit;

namespace FluentSpecifications.Packaging.Tests;

public sealed class PackageContractTests
{
    private const string PackageId = "DanMarshall.FluentSpecifications";
    private const string PackageVersion = "1.0.0";
    private static readonly Lazy<PackageArtifact> Package = new(BuildPackage);

    [Fact]
    public void Package_has_the_public_version_one_identity_and_metadata()
    {
        var artifact = Package.Value;

        Assert.Equal($"{PackageId}.{PackageVersion}.nupkg", Path.GetFileName(artifact.Path));
        Assert.Equal(PackageId, MetadataValue(artifact.Manifest, "id"));
        Assert.Equal(PackageVersion, MetadataValue(artifact.Manifest, "version"));
        Assert.Equal("Dan Marshall", MetadataValue(artifact.Manifest, "authors"));
        Assert.Equal("MIT", MetadataValue(artifact.Manifest, "license"));
        Assert.StartsWith(
            "Specifications for modern C#",
            MetadataValue(artifact.Manifest, "description"),
            StringComparison.Ordinal);
        Assert.Equal(
            "https://fluent-spec.danmarshall.dev",
            MetadataValue(artifact.Manifest, "projectUrl").TrimEnd('/'));
        Assert.Equal(
            "https://github.com/DanMarshall909/fluent-specifications",
            MetadataAttribute(artifact.Manifest, "repository", "url"));
    }

    [Fact]
    public void Package_has_zero_NuGet_dependencies_and_does_not_bundle_platform_assemblies()
    {
        var artifact = Package.Value;
        var dependencyIds = artifact.Manifest
            .Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .Select(element => element.Attribute("id")?.Value)
            .Where(id => id is not null)
            .ToArray();

        Assert.Empty(dependencyIds);
        Assert.DoesNotContain(
            artifact.Entries,
            entry => entry.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(entry).StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            artifact.Entries,
            entry => entry.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(entry).StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Package_contains_the_runtime_generator_documentation_and_symbols()
    {
        var artifact = Package.Value;

        Assert.Contains("lib/net10.0/FluentSpecifications.Core.dll", artifact.Entries);
        Assert.Contains("lib/net10.0/FluentSpecifications.Core.xml", artifact.Entries);
        Assert.Contains(
            "analyzers/dotnet/cs/FluentSpecifications.Generators.dll",
            artifact.Entries);
        Assert.Contains("README.md", artifact.Entries);
        Assert.True(File.Exists(artifact.SymbolPath), "The package must emit a .snupkg symbol package.");
    }

    [Fact]
    public void Package_readme_states_the_dependency_policy_and_acknowledges_prior_art()
    {
        var readme = Package.Value.ReadEntry("README.md");

        Assert.Contains("Zero third-party package dependencies", readme, StringComparison.Ordinal);
        Assert.Contains("Ardalis.Specification", readme, StringComparison.Ordinal);
        Assert.Contains("Spring Data JPA", readme, StringComparison.Ordinal);
        Assert.Contains("RulerZ", readme, StringComparison.Ordinal);
        Assert.Contains("Reapit", readme, StringComparison.Ordinal);
        Assert.Contains("influences, not compatibility targets", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Installed_package_compiles_generated_connector_and_domain_property_syntax()
    {
        var artifact = Package.Value;
        var consumerRoot = Path.Combine(artifact.WorkingDirectory, "consumer");
        Directory.CreateDirectory(consumerRoot);

        File.WriteAllText(
            Path.Combine(consumerRoot, "NuGet.config"),
            $$"""
              <?xml version="1.0" encoding="utf-8"?>
              <configuration>
                <packageSources>
                  <clear />
                  <add key="package-under-test" value="{{SecurityElement.Escape(artifact.OutputDirectory)}}" />
                  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                </packageSources>
              </configuration>
              """);
        File.WriteAllText(
            Path.Combine(consumerRoot, "Consumer.csproj"),
            $$"""
              <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <TargetFramework>net10.0</TargetFramework>
                  <LangVersion>14.0</LangVersion>
                  <Nullable>enable</Nullable>
                  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                </PropertyGroup>
                <ItemGroup>
                  <PackageReference Include="{{PackageId}}" Version="{{PackageVersion}}" />
                </ItemGroup>
              </Project>
              """);
        File.WriteAllText(
            Path.Combine(consumerRoot, "ShippingExample.cs"),
            """
            using FluentSpecifications;

            [SpecificationSet<Order>]
            public static partial class OrderRules
            {
                public static Spec<Order> Paid =>
                    Spec.Define<Order>("order.paid", "Paid", order => order.Paid);

                [Expose]
                public static Spec<Order> CanShip => Paid.Named("order.can-ship", "Can ship");
            }

            public sealed class Order
            {
                public bool Paid { get; init; }
            }

            public static class Shipping
            {
                public static Spec<Order> Ready => OrderRules.CanShip.And.Paid;

                public static bool ShouldShip(Order order) => order.CanShip;
            }
            """);

        RunDotNet(
            consumerRoot,
            "restore",
            "Consumer.csproj",
            "--configfile",
            "NuGet.config");
        RunDotNet(consumerRoot, "build", "Consumer.csproj", "--no-restore", "--nologo");
    }

    private static PackageArtifact BuildPackage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "fluent-specifications-package-tests",
            Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(workingDirectory, "packages");
        Directory.CreateDirectory(outputDirectory);

        RunDotNet(
            repositoryRoot,
            "pack",
            "src/FluentSpecifications.Core/FluentSpecifications.Core.csproj",
            "--configuration",
            "Release",
            "--output",
            outputDirectory,
            "--nologo");

        var packagePath = Directory.GetFiles(outputDirectory, "*.nupkg").Single();
        var symbolPath = Path.ChangeExtension(packagePath, ".snupkg");
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = Assert.Single(
            archive.Entries,
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var manifestStream = manifestEntry.Open();
        var manifest = XDocument.Load(manifestStream);

        return new PackageArtifact(
            packagePath,
            symbolPath,
            workingDirectory,
            outputDirectory,
            manifest,
            archive.Entries
                .Select(entry => entry.FullName)
                .ToHashSet(StringComparer.Ordinal));
    }

    private static string MetadataValue(XDocument manifest, string elementName)
    {
        return Assert.Single(
            manifest.Descendants(),
            element => element.Name.LocalName == elementName).Value;
    }

    private static string MetadataAttribute(
        XDocument manifest,
        string elementName,
        string attributeName)
    {
        var element = Assert.Single(
            manifest.Descendants(),
            candidate => candidate.Name.LocalName == elementName);
        var attribute = element.Attribute(attributeName);
        Assert.NotNull(attribute);
        return attribute.Value;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FluentSpecifications.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static void RunDotNet(string workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutput, standardError);

        Assert.True(
            process.ExitCode == 0,
            $"dotnet {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.\n"
            + standardOutput.Result
            + standardError.Result);
    }

    private sealed record PackageArtifact(
        string Path,
        string SymbolPath,
        string WorkingDirectory,
        string OutputDirectory,
        XDocument Manifest,
        IReadOnlySet<string> Entries)
    {
        public string ReadEntry(string path)
        {
            using var archive = ZipFile.OpenRead(Path);
            var entry = archive.GetEntry(path);
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }
    }
}
