# Programmatic Reference — Automated Coverage Audits

## Contents
- CI metadata validation
- PowerShell audit scripts
- Doc coverage enforcement
- NuGet pack verification

## CI Metadata Validation

Add a GitHub Actions step to fail the build if required metadata is missing:

```yaml
# .github/workflows/ci.yml
- name: Validate NuGet metadata
  shell: pwsh
  run: |
    $csproj = Get-ChildItem -Path "src" -Filter "*.csproj" -Recurse |
      Where-Object { $_.FullName -notmatch "Templates" }

    $errors = @()
    foreach ($file in $csproj) {
      [xml]$proj = Get-Content $file.FullName
      $props = $proj.Project.PropertyGroup
      $flat = $props | ForEach-Object { $_ } | Select-Object -First 1

      $required = @("PackageId", "Description", "PackageTags", "PackageReadmeFile")
      foreach ($field in $required) {
        $val = ($props | ForEach-Object { $_.$field } | Where-Object { $_ }) | Select-Object -First 1
        if (-not $val) {
          $errors += "$($file.Name): missing <$field>"
        }
      }
    }

    if ($errors.Count -gt 0) {
      $errors | ForEach-Object { Write-Error $_ }
      exit 1
    }
```

## PowerShell Audit Script

Run locally to get a full coverage report across all packages:

```powershell
# scripts/audit-metadata.ps1
$projects = Get-ChildItem -Path "src" -Filter "*.csproj" -Recurse |
  Where-Object { $_.Name -notmatch "Templates" }

$report = foreach ($proj in $projects) {
  [xml]$xml = Get-Content $proj.FullName
  $groups = $xml.Project.PropertyGroup

  [PSCustomObject]@{
    Package        = ($groups | ForEach-Object { $_.PackageId }   | Where-Object { $_ }) | Select-Object -First 1
    Description    = ($groups | ForEach-Object { $_.Description } | Where-Object { $_ }) | Select-Object -First 1
    Tags           = ($groups | ForEach-Object { $_.PackageTags } | Where-Object { $_ }) | Select-Object -First 1
    Readme         = ($groups | ForEach-Object { $_.PackageReadmeFile } | Where-Object { $_ }) | Select-Object -First 1
    File           = $proj.Name
  }
}

$report | Format-Table -AutoSize

# Flag gaps
$gaps = $report | Where-Object { -not $_.Description -or -not $_.Tags -or -not $_.Readme }
if ($gaps) {
  Write-Warning "Packages with missing metadata:"
  $gaps | Format-Table File, Description, Tags, Readme
}
```

## XML Doc Coverage Enforcement in CI

```yaml
# .github/workflows/ci.yml
- name: Build with XML docs (enforce CS1591)
  run: |
    dotnet build Modulus.slnx `
      /p:GenerateDocumentationFile=true `
      /p:TreatWarningsAsErrors=false `
      /warnaserror:CS1591 `
      --configuration Release
```

Add to `Directory.Build.props` for per-project control:

```xml
<!-- Enable for abstraction packages only -->
<PropertyGroup Condition="'$(PackageId)' == 'ModulusKit.Mediator.Abstractions' OR
                           '$(PackageId)' == 'ModulusKit.Messaging.Abstractions'">
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
  <NoWarn>$(NoWarn)</NoWarn>
</PropertyGroup>
```

## NuGet Pack Verification Script

After `dotnet pack`, verify all expected fields appear in the generated `.nuspec`:

```powershell
# scripts/verify-pack.ps1
param([string]$NupkgDir = "./nupkgs")

$nupkgs = Get-ChildItem -Path $NupkgDir -Filter "*.nupkg"
foreach ($pkg in $nupkgs) {
  # .nupkg is a zip
  $zip = [System.IO.Compression.ZipFile]::OpenRead($pkg.FullName)
  $nuspec = $zip.Entries | Where-Object { $_.Name -like "*.nuspec" }
  $stream = $nuspec.Open()
  [xml]$spec = (New-Object System.IO.StreamReader($stream)).ReadToEnd()
  $zip.Dispose()

  $meta = $spec.package.metadata
  Write-Host "`n=== $($meta.id) ===" -ForegroundColor Cyan
  Write-Host "Description: $($meta.description)"
  Write-Host "Tags:        $($meta.tags)"
  Write-Host "License:     $($meta.license.'#text')"
  Write-Host "ProjectURL:  $($meta.projectUrl)"
  Write-Host "HasReadme:   $($null -ne $meta.readme)"
}
```

## Iterate-Until-Pass Pattern for Metadata Fixes

1. Run `./scripts/audit-metadata.ps1` — note all gaps
2. Fix identified missing fields in `.csproj` or `Directory.Build.props`
3. Run `dotnet pack --configuration Release --output ./nupkgs`
4. Run `./scripts/verify-pack.ps1` — confirm fields appear in `.nuspec`
5. If gaps remain, return to step 2
6. Only push/publish when all packages pass with zero gaps reported
