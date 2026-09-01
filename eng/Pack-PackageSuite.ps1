[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string] $OutputPath = 'artifacts/packages',

    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}

$packages = @(
    [pscustomobject]@{
        Id = 'DanMarshall.FluentSpecifications'
        Project = 'src/FluentSpecifications.Core/FluentSpecifications.Core.csproj'
    },
    [pscustomobject]@{
        Id = 'DanMarshall.FluentSpecifications.Repositories'
        Project = 'src/FluentSpecifications.Repositories/FluentSpecifications.Repositories.csproj'
    },
    [pscustomobject]@{
        Id = 'DanMarshall.FluentSpecifications.Expressions'
        Project = 'src/FluentSpecifications.Expressions/FluentSpecifications.Expressions.csproj'
    },
    [pscustomobject]@{
        Id = 'DanMarshall.FluentSpecifications.EntityFrameworkCore'
        Project = 'src/FluentSpecifications.EntityFrameworkCore/FluentSpecifications.EntityFrameworkCore.csproj'
    }
)

$selectedVersion = $null
foreach ($package in $packages) {
    $projectPath = Join-Path $repositoryRoot $package.Project
    $propertiesJson = & dotnet msbuild $projectPath `
        -getProperty:PackageId `
        -getProperty:PackageVersion `
        -getProperty:IsPackable `
        -nologo

    if ($LASTEXITCODE -ne 0) {
        throw "Could not read package properties from $($package.Project)."
    }

    $properties = ($propertiesJson | Out-String | ConvertFrom-Json).Properties
    if ($properties.PackageId -ne $package.Id) {
        throw "$($package.Project) must produce $($package.Id), not $($properties.PackageId)."
    }

    if ($properties.IsPackable -ne 'true') {
        throw "$($package.Project) must be packable."
    }

    if ($properties.PackageVersion -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "$($package.Project) has non-release package version '$($properties.PackageVersion)'."
    }

    if ($null -eq $selectedVersion) {
        $selectedVersion = $properties.PackageVersion
    }
    elseif ($properties.PackageVersion -ne $selectedVersion) {
        throw "$($package.Project) version $($properties.PackageVersion) does not match suite version $selectedVersion."
    }
}

New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

foreach ($package in $packages) {
    $packagePath = Join-Path $resolvedOutput "$($package.Id).$selectedVersion.nupkg"
    $symbolPath = Join-Path $resolvedOutput "$($package.Id).$selectedVersion.snupkg"
    foreach ($existingArtifact in @($packagePath, $symbolPath)) {
        if (Test-Path -LiteralPath $existingArtifact) {
            Remove-Item -LiteralPath $existingArtifact -Force
        }
    }

    $arguments = @(
        'pack',
        (Join-Path $repositoryRoot $package.Project),
        '--configuration',
        $Configuration,
        '--output',
        $resolvedOutput,
        '--nologo',
        '-m:1',
        '-nr:false',
        '-p:UseSharedCompilation=false'
    )
    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Packing $($package.Id) failed."
    }

    if (-not (Test-Path -LiteralPath $packagePath)) {
        throw "Packing $($package.Id) did not produce $packagePath."
    }

    if (-not (Test-Path -LiteralPath $symbolPath)) {
        throw "Packing $($package.Id) did not produce $symbolPath."
    }
}

Write-Output "Packed $($packages.Count) packages at coordinated version $selectedVersion into $resolvedOutput."
