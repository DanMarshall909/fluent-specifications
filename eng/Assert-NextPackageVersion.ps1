[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $PackageId,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $CandidateVersion,

    [ValidateNotNullOrEmpty()]
    [string] $FirstStableVersion = '1.0.0',

    [switch] $AllowAlreadyPublished,

    [string] $PublishedVersionsJson,

    [ValidateNotNullOrEmpty()]
    [string] $PackageIndexUri
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-StableSemanticVersion {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    if ($Value -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        throw "'$Value' is not a stable semantic version in major.minor.patch form."
    }

    [pscustomobject]@{
        Value = $Value
        Major = [int] $Matches[1]
        Minor = [int] $Matches[2]
        Patch = [int] $Matches[3]
    }
}

$candidate = ConvertTo-StableSemanticVersion $CandidateVersion
$firstStable = ConvertTo-StableSemanticVersion $FirstStableVersion

if ($PSBoundParameters.ContainsKey('PublishedVersionsJson')) {
    $decodedVersions = ConvertFrom-Json -InputObject $PublishedVersionsJson
    $publishedVersions = @($decodedVersions)
}
else {
    $normalizedPackageId = $PackageId.ToLowerInvariant()
    $indexUri = if ($PSBoundParameters.ContainsKey('PackageIndexUri')) {
        $PackageIndexUri
    }
    else {
        "https://api.nuget.org/v3-flatcontainer/$normalizedPackageId/index.json"
    }

    try {
        $index = Invoke-RestMethod -Uri $indexUri
        $publishedVersions = @($index.versions)
    }
    catch {
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        $response = if ($null -ne $responseProperty) {
            $responseProperty.Value
        }
        else {
            $null
        }
        $statusCodeProperty = if ($null -ne $response) {
            $response.PSObject.Properties['StatusCode']
        }
        else {
            $null
        }

        if ($null -ne $statusCodeProperty -and
            [int] $statusCodeProperty.Value -eq 404) {
            $publishedVersions = @()
        }
        else {
            throw
        }
    }
}

if ($publishedVersions -contains $CandidateVersion) {
    if ($AllowAlreadyPublished) {
        Write-Output "$PackageId $CandidateVersion is already published; accepting the idempotent retry."
        exit 0
    }

    throw "$PackageId $CandidateVersion has already been published."
}

$stablePublishedVersions = @(
    foreach ($publishedVersion in $publishedVersions) {
        if ($publishedVersion -match '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
            ConvertTo-StableSemanticVersion $publishedVersion
        }
    }
)

$latest = $stablePublishedVersions |
    Sort-Object -Property Major, Minor, Patch -Descending |
    Select-Object -First 1

if ($null -eq $latest) {
    if ($CandidateVersion -ne $firstStable.Value) {
        throw "The first stable version of $PackageId must be $($firstStable.Value), not $CandidateVersion."
    }

    Write-Output "Verified $PackageId $CandidateVersion as the first stable release."
    exit 0
}

$isNextPatch =
    $candidate.Major -eq $latest.Major -and
    $candidate.Minor -eq $latest.Minor -and
    $candidate.Patch -eq ($latest.Patch + 1)
$isNextMinor =
    $candidate.Major -eq $latest.Major -and
    $candidate.Minor -eq ($latest.Minor + 1) -and
    $candidate.Patch -eq 0
$isNextMajor =
    $candidate.Major -eq ($latest.Major + 1) -and
    $candidate.Minor -eq 0 -and
    $candidate.Patch -eq 0

if (-not ($isNextPatch -or $isNextMinor -or $isNextMajor)) {
    throw "$CandidateVersion is not the next patch, minor, or major release after $($latest.Value)."
}

Write-Output "Verified $PackageId $CandidateVersion as the next release after $($latest.Value)."
