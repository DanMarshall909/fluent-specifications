using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace FluentSpecifications.Packaging.Tests;

public sealed class PackageContractTests
{
    private const string PackageVersion = "1.2.0";
    private const string CorePackageId = "DanMarshall.FluentSpecifications";
    private const string RepositoryPackageId =
        "DanMarshall.FluentSpecifications.Repositories";
    private const string ExpressionPackageId =
        "DanMarshall.FluentSpecifications.Expressions";
    private const string EntityFrameworkPackageId =
        "DanMarshall.FluentSpecifications.EntityFrameworkCore";

    private static readonly PackageExpectation[] Expectations =
    [
        new(CorePackageId, "FluentSpecifications.Core", []),
        new(
            RepositoryPackageId,
            "FluentSpecifications.Repositories",
            [CorePackageId]),
        new(
            ExpressionPackageId,
            "FluentSpecifications.Expressions",
            [CorePackageId]),
        new(
            EntityFrameworkPackageId,
            "FluentSpecifications.EntityFrameworkCore",
            [
                CorePackageId,
                RepositoryPackageId,
                ExpressionPackageId,
                "Microsoft.EntityFrameworkCore.Relational"
            ])
    ];

    private static readonly Lazy<PackageSuite> Packages = new(BuildPackages);

    [Fact]
    public void Package_suite_has_coordinated_release_identity_and_metadata()
    {
        var suite = Packages.Value;

        Assert.Equal(Expectations.Length, suite.Artifacts.Count);
        foreach (var expectation in Expectations)
        {
            var artifact = suite[expectation.PackageId];

            Assert.Equal(
                $"{expectation.PackageId}.{PackageVersion}.nupkg",
                Path.GetFileName(artifact.Path));
            Assert.Equal(expectation.PackageId, MetadataValue(artifact.Manifest, "id"));
            Assert.Equal(PackageVersion, MetadataValue(artifact.Manifest, "version"));
            Assert.Equal("Dan Marshall", MetadataValue(artifact.Manifest, "authors"));
            Assert.Equal("MIT", MetadataValue(artifact.Manifest, "license"));
            Assert.Equal(
                "https://github.com/DanMarshall909/fluent-specifications",
                MetadataAttribute(artifact.Manifest, "repository", "url"));
            Assert.True(
                MetadataValue(artifact.Manifest, "description").Length >= 70,
                $"{expectation.PackageId} must have a useful package description.");
        }
    }

    [Fact]
    public void Starter_package_remains_dependency_free_and_bundles_no_platform_assemblies()
    {
        var artifact = Packages.Value[CorePackageId];

        Assert.Empty(Dependencies(artifact.Manifest));
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
    public void Extension_dependencies_preserve_the_provider_boundary()
    {
        foreach (var expectation in Expectations)
        {
            var dependencies = Dependencies(Packages.Value[expectation.PackageId].Manifest);

            Assert.Equal(
                expectation.Dependencies.Order(StringComparer.Ordinal),
                dependencies.Keys.Order(StringComparer.Ordinal));
            foreach (var dependency in expectation.Dependencies)
            {
                var expectedVersion = dependency.StartsWith(
                    "DanMarshall.",
                    StringComparison.Ordinal)
                    ? PackageVersion
                    : "10.0.0";
                Assert.Equal(expectedVersion, dependencies[dependency]);
            }
        }

        var repositoryDependencies = Dependencies(
            Packages.Value[RepositoryPackageId].Manifest);
        Assert.DoesNotContain(
            repositoryDependencies.Keys,
            dependency => dependency.Contains(
                "EntityFrameworkCore",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_package_contains_runtime_documentation_readme_and_symbols()
    {
        foreach (var expectation in Expectations)
        {
            var artifact = Packages.Value[expectation.PackageId];

            Assert.Contains(
                $"lib/net10.0/{expectation.AssemblyName}.dll",
                artifact.Entries);
            Assert.Contains(
                $"lib/net10.0/{expectation.AssemblyName}.xml",
                artifact.Entries);
            Assert.Contains("README.md", artifact.Entries);
            Assert.True(
                File.Exists(artifact.SymbolPath),
                $"{expectation.PackageId} must emit a .snupkg symbol package.");
        }

        Assert.Contains(
            "analyzers/dotnet/cs/FluentSpecifications.Generators.dll",
            Packages.Value[CorePackageId].Entries);
    }

    [Fact]
    public void Package_readme_states_installation_boundaries_and_acknowledges_prior_art()
    {
        var readme = Packages.Value[CorePackageId].ReadEntry("README.md");

        Assert.Contains("Zero third-party package dependencies", readme, StringComparison.Ordinal);
        Assert.Contains(RepositoryPackageId, readme, StringComparison.Ordinal);
        Assert.Contains(EntityFrameworkPackageId, readme, StringComparison.Ordinal);
        Assert.Contains("Ardalis.Specification", readme, StringComparison.Ordinal);
        Assert.Contains("Spring Data JPA", readme, StringComparison.Ordinal);
        Assert.Contains("RulerZ", readme, StringComparison.Ordinal);
        Assert.Contains("Reapit", readme, StringComparison.Ordinal);
        Assert.Contains("influences, not compatibility targets", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Installed_package_suite_compiles_generated_repository_and_ef_syntax()
    {
        var suite = Packages.Value;
        var consumerRoot = Path.Combine(suite.WorkingDirectory, "consumer");
        var externalFeed = Path.Combine(suite.WorkingDirectory, "restored-dependencies");
        Directory.CreateDirectory(consumerRoot);
        CopyRestoredDependencies(suite.RepositoryRoot, externalFeed);

        File.WriteAllText(
            Path.Combine(consumerRoot, "NuGet.config"),
            $$"""
              <?xml version="1.0" encoding="utf-8"?>
              <configuration>
                <config>
                  <add key="globalPackagesFolder" value="{{SecurityElement.Escape(Path.Combine(consumerRoot, "packages-cache"))}}" />
                </config>
                <packageSources>
                  <clear />
                  <add key="packages-under-test" value="{{SecurityElement.Escape(suite.OutputDirectory)}}" />
                  <add key="restored-dependencies" value="{{SecurityElement.Escape(externalFeed)}}" />
                </packageSources>
                <packageSourceMapping>
                  <packageSource key="packages-under-test">
                    <package pattern="DanMarshall.*" />
                  </packageSource>
                  <packageSource key="restored-dependencies">
                    <package pattern="Microsoft.*" />
                  </packageSource>
                </packageSourceMapping>
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
                  <NuGetAudit>false</NuGetAudit>
                </PropertyGroup>
                <ItemGroup>
                  <PackageReference Include="{{CorePackageId}}" Version="{{PackageVersion}}" />
                  <PackageReference Include="{{EntityFrameworkPackageId}}" Version="{{PackageVersion}}" />
                </ItemGroup>
              </Project>
              """);
        File.WriteAllText(
            Path.Combine(consumerRoot, "ShippingExample.cs"),
            """
            using FluentSpecifications;
            using FluentSpecifications.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore;

            [SpecificationSet<Order>(GenerateSearch = true)]
            public static partial class OrderRules
            {
                public static Spec<Order> Paid =>
                    Spec.Define<Order>("order.paid", "Paid", order => order.Paid);

                [Expose]
                public static Spec<Order> CanShip =>
                    Paid.Named("order.can-ship", "Can ship");
            }

            public sealed class Order
            {
                public int Id { get; init; }

                public bool Paid { get; init; }
            }

            public static class Shipping
            {
                public static IReadRepository<Order> CreateRepository(DbContext context) =>
                    new EntityFrameworkRepository<Order>(context);

                public static Search<Order> PaidOrders => Order.Search.Matching.CanShip;

                public static bool ShouldShip(Order order) => order.CanShip;
            }
            """);

        RunProcess(
            consumerRoot,
            "dotnet",
            "restore",
            "Consumer.csproj",
            "--configfile",
            "NuGet.config");
        RunProcess(
            consumerRoot,
            "dotnet",
            "build",
            "Consumer.csproj",
            "--no-restore",
            "--nologo",
            "-m:1",
            "-nr:false",
            "-p:UseSharedCompilation=false");
    }

    private static PackageSuite BuildPackages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "fluent-specifications-package-tests",
            Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(workingDirectory, "packages");
        Directory.CreateDirectory(outputDirectory);

        RunProcess(
            repositoryRoot,
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(repositoryRoot, "eng", "Pack-PackageSuite.ps1"),
            "-Configuration",
            "Release",
            "-OutputPath",
            outputDirectory,
            "-NoRestore");

        var packagePaths = Directory.GetFiles(outputDirectory, "*.nupkg");
        Assert.Equal(Expectations.Length, packagePaths.Length);
        var artifacts = packagePaths
            .Select(ReadArtifact)
            .ToDictionary(
                artifact => MetadataValue(artifact.Manifest, "id"),
                StringComparer.Ordinal);

        return new PackageSuite(
            repositoryRoot,
            workingDirectory,
            outputDirectory,
            artifacts);
    }

    private static PackageArtifact ReadArtifact(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var manifestEntry = Assert.Single(
            archive.Entries,
            entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
        using var manifestStream = manifestEntry.Open();
        var manifest = XDocument.Load(manifestStream);
        var symbolPath = Path.Combine(
            Path.GetDirectoryName(packagePath)!,
            $"{Path.GetFileNameWithoutExtension(packagePath)}.snupkg");

        return new PackageArtifact(
            packagePath,
            symbolPath,
            manifest,
            archive.Entries
                .Select(entry => entry.FullName)
                .ToHashSet(StringComparer.Ordinal));
    }

    private static IReadOnlyDictionary<string, string> Dependencies(XDocument manifest) =>
        manifest
            .Descendants()
            .Where(element => element.Name.LocalName == "dependency")
            .ToDictionary(
                element => element.Attribute("id")?.Value
                    ?? throw new InvalidDataException("A dependency has no package ID."),
                element => element.Attribute("version")?.Value
                    ?? throw new InvalidDataException("A dependency has no version."),
                StringComparer.Ordinal);

    private static string MetadataValue(XDocument manifest, string elementName) =>
        Assert.Single(
            manifest.Descendants(),
            element => element.Name.LocalName == elementName).Value;

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

    private static void CopyRestoredDependencies(
        string repositoryRoot,
        string destination)
    {
        Directory.CreateDirectory(destination);
        var assetsPath = Path.Combine(
            repositoryRoot,
            "src",
            "FluentSpecifications.EntityFrameworkCore",
            "obj",
            "project.assets.json");
        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var packageFolder = assets.RootElement
            .GetProperty("packageFolders")
            .EnumerateObject()
            .Select(folder => folder.Name)
            .First(Directory.Exists);

        foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() != "package")
            {
                continue;
            }

            var relativePath = library.Value.GetProperty("path").GetString()
                ?? throw new InvalidDataException($"{library.Name} has no restored path.");
            var packageDirectory = Path.Combine(
                packageFolder,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            var archive = Directory.GetFiles(packageDirectory, "*.nupkg").Single();
            File.Copy(archive, Path.Combine(destination, Path.GetFileName(archive)), true);
        }
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

    private static void RunProcess(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
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
            $"{fileName} {string.Join(' ', arguments)} failed with exit code {process.ExitCode}.\n"
                + standardOutput.Result
                + standardError.Result);
    }

    private sealed record PackageExpectation(
        string PackageId,
        string AssemblyName,
        IReadOnlyList<string> Dependencies);

    private sealed record PackageSuite(
        string RepositoryRoot,
        string WorkingDirectory,
        string OutputDirectory,
        IReadOnlyDictionary<string, PackageArtifact> Artifacts)
    {
        public PackageArtifact this[string packageId] => Artifacts[packageId];
    }

    private sealed record PackageArtifact(
        string Path,
        string SymbolPath,
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
