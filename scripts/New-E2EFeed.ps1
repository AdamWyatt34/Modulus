<#
.SYNOPSIS
Packs the repository's HEAD into a local NuGet feed for E2E testing.

.DESCRIPTION
The E2E tests (Category=E2E) scaffold a solution whose Directory.Packages.props pins
ModulusKit.* to a specific version. At an untagged HEAD that version is unpublished, so
restore against nuget.org fails. This script packs all packages at a synthetic version
into a local folder feed; the E2E tests pick it up via two environment variables.

The default version is timestamped so repeated local runs never collide with entries
already extracted into the NuGet global package cache.

.EXAMPLE
pwsh scripts/New-E2EFeed.ps1
dotnet test Modulus.slnx --filter "Category=E2E"
#>
param(
    [string]$Version = "99.0.0-e2e.$(Get-Date -Format yyyyMMddHHmmss)",
    [string]$Output = "artifacts/e2e-feed"
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$feedPath = Join-Path $repoRoot $Output

dotnet pack (Join-Path $repoRoot 'Modulus.slnx') --configuration Release /p:MinVerVersionOverride=$Version --output $feedPath
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE" }

$env:MODULUS_E2E_FEED = $feedPath
$env:MODULUS_E2E_PACKAGE_VERSION = $Version

Write-Host ""
Write-Host "Local E2E feed ready. Environment configured for this session:" -ForegroundColor Green
Write-Host "  MODULUS_E2E_FEED            = $feedPath"
Write-Host "  MODULUS_E2E_PACKAGE_VERSION = $Version"
Write-Host ""
Write-Host "Run: dotnet test Modulus.slnx --filter `"Category=E2E`""
