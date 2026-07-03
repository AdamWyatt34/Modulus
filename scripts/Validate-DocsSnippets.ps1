#!/usr/bin/env pwsh
#Requires -Version 7.0
<#
.SYNOPSIS
    Compile-validates C# snippets in the documentation.

.DESCRIPTION
    Scans docs/messaging, docs/mediator, and docs/getting-started for fenced
    ```csharp blocks whose immediately preceding non-empty line is the marker
    comment `<!-- verify -->`. Each marked snippet is emitted into a scratch
    classlib project (referencing the real Modulus source projects) and the
    whole set is compiled with `dotnet build`.

    Build errors are mapped back to the source markdown file and line via
    #line directives, so a failure report points at the doc, not the scratch
    project.

.EXAMPLE
    pwsh scripts/Validate-DocsSnippets.ps1

.NOTES
    Exit codes: 0 = all marked snippets compile, 1 = failure (including the
    case where zero marked snippets are found, which guards against the
    marker convention silently rotting).
#>
[CmdletBinding()]
param(
    # Keep the scratch project on disk after the run (for debugging).
    [switch]$KeepScratch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Marker = '<!-- verify -->'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$DocRoots = @('docs/messaging', 'docs/mediator', 'docs/getting-started')

# ---------------------------------------------------------------------------
# 1. Collect marked snippets
# ---------------------------------------------------------------------------
$snippets = [System.Collections.Generic.List[object]]::new()

foreach ($relRoot in $DocRoots) {
    $root = Join-Path $RepoRoot $relRoot
    if (-not (Test-Path -LiteralPath $root)) { continue }

    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -Filter '*.md' | Sort-Object FullName) {
        $lines = @(Get-Content -LiteralPath $file.FullName)
        $indexInFile = 0

        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -notmatch '^```csharp\b') { continue }

            # Find the closing fence.
            $end = $i + 1
            while ($end -lt $lines.Count -and $lines[$end] -notmatch '^```\s*$') { $end++ }

            # Immediately preceding non-empty line must be the marker.
            $j = $i - 1
            while ($j -ge 0 -and [string]::IsNullOrWhiteSpace($lines[$j])) { $j-- }

            if ($j -ge 0 -and $lines[$j].Trim() -eq $Marker) {
                $indexInFile++
                $code = if ($end - $i -gt 1) { $lines[($i + 1)..($end - 1)] } else { @() }
                $snippets.Add([pscustomobject]@{
                    FullPath    = $file.FullName
                    RelPath     = [IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
                    IndexInFile = $indexInFile
                    CodeStart   = $i + 2      # 1-based line number of the first code line
                    CodeEnd     = $end        # 1-based line number of the closing fence
                    Code        = @($code)
                })
            }

            $i = $end
        }
    }
}

if ($snippets.Count -eq 0) {
    Write-Host "ERROR: No snippets marked with '$Marker' were found under $($DocRoots -join ', ')." -ForegroundColor Red
    Write-Host 'Either the marker convention has rotted or the docs were moved. Refusing to report success.' -ForegroundColor Red
    exit 1
}

Write-Host "Found $($snippets.Count) marked snippet(s) in $(@($snippets.RelPath | Select-Object -Unique).Count) file(s)."

# ---------------------------------------------------------------------------
# 2. Create the scratch project
# ---------------------------------------------------------------------------
$scratch = Join-Path ([IO.Path]::GetTempPath()) 'modulus-docs-snippets'
if (Test-Path -LiteralPath $scratch) {
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
New-Item -ItemType Directory -Path $scratch | Out-Null

$srcRoot = (Join-Path $RepoRoot 'src').Replace('\', '/')

$projectReferences = @(
    'Modulus.Mediator.Abstractions/Modulus.Mediator.Abstractions.csproj'
    'Modulus.Mediator/Modulus.Mediator.csproj'
    'Modulus.Messaging.Abstractions/Modulus.Messaging.Abstractions.csproj'
    'Modulus.Messaging/Modulus.Messaging.csproj'
    'Modulus.Messaging.RabbitMq/Modulus.Messaging.RabbitMq.csproj'
    'Modulus.Messaging.AzureServiceBus/Modulus.Messaging.AzureServiceBus.csproj'
) | ForEach-Object { "    <ProjectReference Include=`"$srcRoot/$_`" />" }

$csproj = @"
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <NoWarn>`$(NoWarn);CS1998;CS8321;CS0219;CS0162;CS4014;CA2007</NoWarn>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
$($projectReferences -join "`n")
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FluentValidation" Version="12.1.1" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.3" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.3" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="10.0.3" />
  </ItemGroup>

</Project>
"@
Set-Content -LiteralPath (Join-Path $scratch 'DocsSnippets.csproj') -Value $csproj -Encoding utf8NoBOM

$globalUsings = @(
    'System'
    'System.Threading'
    'System.Threading.Tasks'
    'Microsoft.AspNetCore.Builder'
    'Microsoft.Extensions.Configuration'
    'Microsoft.Extensions.DependencyInjection'
    'Microsoft.Extensions.Hosting'
    'Microsoft.EntityFrameworkCore'
    'FluentValidation'
    'Modulus.Mediator'
    'Modulus.Mediator.Abstractions'
    'Modulus.Mediator.Behaviors'
    'Modulus.Messaging'
    'Modulus.Messaging.Abstractions'
    'Modulus.Messaging.RabbitMq'
    'Modulus.Messaging.AzureServiceBus'
) | ForEach-Object { "global using $_;" }
Set-Content -LiteralPath (Join-Path $scratch 'GlobalUsings.cs') -Value ($globalUsings -join "`n") -Encoding utf8NoBOM

# Ambient types that doc snippets conventionally reference. In a real host the
# entry point provides `Program` (used as `typeof(Program).Assembly`).
$support = @'
public partial class Program;
'@
Set-Content -LiteralPath (Join-Path $scratch 'Support.cs') -Value $support -Encoding utf8NoBOM

# ---------------------------------------------------------------------------
# 3. Emit one Snippet_<n>.cs per marked snippet
# ---------------------------------------------------------------------------
# Any line opening a top-level type declaration means the snippet is emitted
# as-is inside a namespace; otherwise it is treated as statements and wrapped
# in a Run(...) method.
$typeDeclPattern = '(?m)^\s*(\[|public |internal |sealed |abstract |static |partial |record |class |interface |enum )'
$usingPattern = '^\s*using\s+(static\s+)?[A-Za-z_][\w.]*\s*;\s*$'

$n = 0
foreach ($snippet in $snippets) {
    $n++
    # The #line path is embedded in the generated file; forward slashes keep it
    # readable and cross-platform.
    $mdPath = $snippet.FullPath.Replace('\', '/')

    # Hoist leading `using X;` lines above the namespace declaration.
    $usings = [System.Collections.Generic.List[string]]::new()
    $bodyLines = [System.Collections.Generic.List[string]]::new()
    $inHeader = $true
    $bodyStartOffset = 0   # offset (in snippet lines) of the first body line

    for ($k = 0; $k -lt $snippet.Code.Count; $k++) {
        $line = $snippet.Code[$k]
        if ($inHeader -and [string]::IsNullOrWhiteSpace($line)) { continue }
        if ($inHeader -and $line -match $usingPattern) {
            $usings.Add($line.Trim())
            continue
        }
        if ($inHeader) {
            $inHeader = $false
            $bodyStartOffset = $k
        }
        $bodyLines.Add($line)
    }

    $body = $bodyLines -join "`n"
    $bodyStartLine = $snippet.CodeStart + $bodyStartOffset
    $isTypeDecl = $body -match $typeDeclPattern

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("// Source: $($snippet.RelPath) (snippet $($snippet.IndexInFile), line $($snippet.CodeStart))")
    if ($usings.Count -gt 0) {
        [void]$sb.AppendLine("#line $($snippet.CodeStart) `"$mdPath`"")
        foreach ($u in $usings) { [void]$sb.AppendLine($u) }
        [void]$sb.AppendLine('#line default')
    }
    [void]$sb.AppendLine("namespace Docs.Snippets.N$n;")
    [void]$sb.AppendLine()

    if ($isTypeDecl) {
        [void]$sb.AppendLine("#line $bodyStartLine `"$mdPath`"")
        [void]$sb.AppendLine($body)
        [void]$sb.AppendLine('#line default')
    }
    else {
        # Statement snippet: wrap in a method with the ambient objects doc
        # snippets conventionally use. Parameters the snippet declares itself
        # (var builder = ..., var app = ...) are omitted to avoid CS0136.
        $params = [System.Collections.Generic.List[string]]::new()
        if ($body -match '\bargs\b') { $params.Add('string[] args') }
        if ($body -notmatch '(?m)^\s*var\s+builder\s*=') { $params.Add('WebApplicationBuilder builder') }
        if ($body -notmatch '(?m)^\s*var\s+app\s*=') { $params.Add('WebApplication app') }
        if ($body -notmatch '(?m)^\s*var\s+services\s*=') { $params.Add('IServiceCollection services') }
        $params.Add('IConfiguration configuration')
        $params.Add('IMessageBus messageBus')
        $params.Add('IMediator mediator')
        $params.Add('CancellationToken cancellationToken')

        [void]$sb.AppendLine('public static class Snippet')
        [void]$sb.AppendLine('{')
        [void]$sb.AppendLine("    public static async Task Run($($params -join ', '))")
        [void]$sb.AppendLine('    {')
        [void]$sb.AppendLine("#line $bodyStartLine `"$mdPath`"")
        [void]$sb.AppendLine($body)
        [void]$sb.AppendLine('#line default')
        [void]$sb.AppendLine('    }')
        [void]$sb.AppendLine('}')
    }

    Set-Content -LiteralPath (Join-Path $scratch "Snippet_$n.cs") -Value $sb.ToString() -Encoding utf8NoBOM
}

# ---------------------------------------------------------------------------
# 4. Build
# ---------------------------------------------------------------------------
Write-Host "Building scratch project ($scratch)..."
$buildOutput = & dotnet build (Join-Path $scratch 'DocsSnippets.csproj') -nologo --verbosity quiet 2>&1 |
    ForEach-Object { "$_" }
$buildExitCode = $LASTEXITCODE

# ---------------------------------------------------------------------------
# 5. Report
# ---------------------------------------------------------------------------
if ($buildExitCode -ne 0) {
    Write-Host ''
    Write-Host 'DOC SNIPPET COMPILATION FAILED' -ForegroundColor Red
    Write-Host '==============================' -ForegroundColor Red

    # Thanks to #line directives, compiler errors point directly at the
    # markdown files. Map each error line back to its snippet.
    $errorPattern = '^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\)\s*:\s*error\s+(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.*?)(\s+\[[^\[\]]+\])?$'
    $reported = [System.Collections.Generic.HashSet[string]]::new()
    $mappedAny = $false

    foreach ($line in $buildOutput) {
        if ($line -notmatch $errorPattern) { continue }
        $errFile = $Matches['file'].Replace('\', '/')
        $errLine = [int]$Matches['line']
        $key = "$errFile($errLine): $($Matches['code'])"
        if (-not $reported.Add($key)) { continue }

        $owner = $snippets | Where-Object {
            $_.FullPath.Replace('\', '/') -eq $errFile -and
            $errLine -ge $_.CodeStart -and $errLine -le $_.CodeEnd
        } | Select-Object -First 1

        $mappedAny = $true
        if ($null -ne $owner) {
            Write-Host ("  {0}:{1} (snippet #{2}) error {3}: {4}" -f `
                $owner.RelPath, $errLine, $owner.IndexInFile, $Matches['code'], $Matches['msg']) -ForegroundColor Red
        }
        else {
            Write-Host ("  {0}({1}): error {2}: {3}" -f $errFile, $errLine, $Matches['code'], $Matches['msg']) -ForegroundColor Red
        }
    }

    if (-not $mappedAny) {
        # No parseable compiler errors (e.g. restore failure) - dump raw output.
        $buildOutput | ForEach-Object { Write-Host "  $_" }
    }

    Write-Host ''
    Write-Host "Scratch project left at: $scratch" -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host "All $($snippets.Count) marked doc snippet(s) compiled successfully." -ForegroundColor Green
Write-Host ''
Write-Host 'Per-file breakdown:'
$snippets | Group-Object RelPath | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0}: {1} snippet(s)" -f $_.Name, $_.Count)
}

if (-not $KeepScratch) {
    Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
}

exit 0
