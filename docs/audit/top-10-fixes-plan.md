# Top 10 Fixes — Implementation Plan

Implementation plan for the ten HIGH-severity findings from [`code-review.md`](code-review.md). Bundled into five sequenced PRs that can each be reviewed and shipped independently.

**Decisions locked in:**

- **Issue #3 (UnitOfWorkBehavior)** — ship in `Modulus.Mediator`. The scaffold's template-shipped copies are deleted and the template imports from `Modulus.Mediator.Behaviors` instead.
- **Issue #4 (MassTransit)** — stay on v7.3.1. v8 moved to a paid commercial license. The "fix" becomes pin + document rationale + add CVE scanning.

---

## PR1 — Template Security Defaults (#1, #2, #3-template-side)

**Files**: `src/Modulus.Templates/templates/init/host/Program.cs.template`, `src/Modulus.Templates/templates/init/host/appsettings.json.template`, `src/Modulus.Templates/templates/init/building-blocks/application/DependencyInjection/ApplicationServiceExtensions.cs.template`, `src/Modulus.Templates/templates/module/src/ModuleName.Api/Endpoints/GetSample.cs.template`, `src/Modulus.Templates/templates/module/src/ModuleName.Api/Endpoints/ModuleNameEndpointRegistration.cs.template`, plus `BaseDbContext.cs.template`, `ModuleNameModule.cs.template`, and `ResourceManifest.cs` for the UoW consolidation.

### #1 Gate Scalar / OpenAPI on `IsDevelopment()`

`Program.cs.template:30-31` becomes:

```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
```

### #2 Scaffold authentication and authorization stubs

In `Program.cs.template`, add after `AddOpenApi()`:

```csharp
// Configure your authentication scheme (JWT bearer, OIDC, Keycloak, etc.) before AddAuthorization.
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();
```

After `app.UseStatusCodePages()`:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

In `ModuleNameEndpointRegistration.cs.template` add `.RequireAuthorization()` to the module group:

```csharp
var group = app.MapGroup("/api/{{ModuleNameLower}}")
    .RequireAuthorization();
```

Add a comment in `GetSample.cs.template` explaining that the group requires authorization and individual endpoints opt out with `.AllowAnonymous()`. Change the sample route from `"/"` to `"/sample"` so it doesn't collide with the group root.

### #3 (template side) Consolidate UoW on the library

The template currently ships `IUnitOfWork.cs.template` and `UnitOfWorkBehavior.cs.template`. Delete both, remove the two `ResourceManifest.cs` entries, and update imports:

- `ApplicationServiceExtensions.cs.template` — remove `using {{RootNamespace}}.BuildingBlocks.Application.Behaviors;`. The `UnitOfWorkBehavior<,>` on line 19 now resolves from `Modulus.Mediator.Behaviors` (imported on line 3).
- `BaseDbContext.cs.template` — remove `using {{RootNamespace}}.BuildingBlocks.Application;`. `IUnitOfWork` resolves from `Modulus.Mediator.Abstractions` (already imported on line 2).
- `ModuleNameModule.cs.template` — change `using {{RootNamespace}}.BuildingBlocks.Application;` to `using Modulus.Mediator.Abstractions;`.

### Template hardening alongside

- `appsettings.json.template:3` — change `TrustServerCertificate=true` to `Encrypt=false` (dev-only; production should override). Narrow `AllowedHosts: "*"` to `"localhost"` on line 12.

### Tests

Extend `tests/Modulus.Cli.Tests/Handlers/InitHandlerTests.cs` and `AddModuleHandlerTests.cs` with snapshot-style assertions:

- `Init_Program_cs_gates_openapi_and_scalar_on_IsDevelopment`
- `Init_Program_cs_wires_authentication_and_authorization`
- `Init_ApplicationServiceExtensions_registers_canonical_pipeline_order` (five behaviors in order, import from `Modulus.Mediator.Behaviors`)
- `Init_appsettings_uses_narrow_AllowedHosts`
- `Init_appsettings_does_not_ship_TrustServerCertificate`
- `Init_does_not_emit_local_UnitOfWorkBehavior_template`
- `AddModule_EndpointRegistration_requires_authorization_on_group`
- `AddModule_GetSample_uses_sample_route_not_root`
- `AddModule_imports_IUnitOfWork_from_library_namespace`

### Verification

```powershell
$dir = Join-Path $env:TEMP "modulus-pr1-$(Get-Random)"
dotnet run --project D:\personal\Modulus\src\Modulus.Cli\Modulus.Cli.csproj -- init Sample --output $dir
Select-String -Path (Join-Path $dir "Sample\src\Sample.WebApi\Program.cs") -Pattern "IsDevelopment"
Select-String -Path (Join-Path $dir "Sample\src\Sample.WebApi\Program.cs") -Pattern "UseAuthentication"
```

---

## PR2 — Ship UnitOfWorkBehavior (#3 library side)

**New** `src/Modulus.Mediator.Abstractions/Persistence/IUnitOfWork.cs`:

```csharp
namespace Modulus.Mediator.Abstractions;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

**New** `src/Modulus.Mediator/Behaviors/UnitOfWorkBehavior.cs`:

```csharp
public sealed class UnitOfWorkBehavior<TRequest, TResponse>(IServiceProvider serviceProvider)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);

        if (!IsCommand(request) || !response.IsSuccess)
            return response;

        var uow = serviceProvider.GetService<IUnitOfWork>();
        if (uow is not null)
            await uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }

    private static bool IsCommand(TRequest request) { /* check ICommand and ICommand<> */ }
}
```

Resolve via `GetService` (not `GetRequiredService`) so consumers without a UoW get a no-op, matching `IInboxStore`. Only commits on successful commands; queries and failures bypass.

### Tests in `tests/Modulus.Mediator.Tests/Behaviors/UnitOfWorkBehaviorTests.cs`

- `Handle_SuccessfulCommand_CallsSaveChanges`
- `Handle_FailedCommand_DoesNotCallSaveChanges`
- `Handle_Query_DoesNotCallSaveChanges`
- `Handle_NoUnitOfWorkRegistered_DoesNotThrow`
- `Handle_PassesCancellationToken_ToSaveChanges`
- `Handle_CommandWithResult_CallsSaveChanges`

---

## PR3 — CLI Hardening (#5, #7)

### #5 Path containment

New `src/Modulus.Cli/Infrastructure/PathGuard.cs`:

```csharp
internal static class PathGuard
{
    public static string EnsureContained(string baseDirectory, string relativePath)
    {
        var canonicalBase = Path.GetFullPath(baseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));

        if (!fullPath.StartsWith(canonicalBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Path traversal detected: '{relativePath}' resolves outside '{baseDirectory}'.");

        return fullPath;
    }
}
```

Apply in `InitHandler.cs:57`, `AddModuleHandler.cs:92`, `AddCommandHandler.cs:82`, `AddQueryHandler.cs:82`, `AddEntityHandler.cs:95`, `AddEndpointHandler.cs:121`.

### #7 ProcessRunner ArgumentList migration

Widen `IProcessRunner.RunAsync`:

```csharp
public interface IProcessRunner
{
    Task<int> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
```

`ProcessRunner` uses `ProcessStartInfo.ArgumentList.Add(arg)` per arg and passes the token to `WaitForExitAsync`. Update all six call sites (`InitHandler.cs:67,75,82,83`; `AddModuleHandler.cs:109,138`). Example:

```csharp
processRunner.RunAsync("git", ["commit", "-m", "Initial commit from Modulus"], solutionRoot);
```

Update `FakeProcessRunner` to the new signature; tests asserting `Calls[0].Arguments` switch to a collection.

### Tests

- `tests/Modulus.Cli.Tests/Infrastructure/PathGuardTests.cs` — happy path; `../escape.txt` throws; absolute escape throws; trailing-separator consistency.
- `tests/Modulus.Cli.Tests/Infrastructure/ProcessRunnerTests.cs` — new; spawn `dotnet --version` cross-platform; cancellation cancels a long spawn.
- Extend `CSharpIdentifierValidatorTests` with one assertion per rejected character (`<`, `>`, `&`, `"`, `'`, `;`, `{`, `}`, `:`, `(`, `)`, `.`, space), each commented with the threat it neutralizes. This documents the implicit contract so a future contributor can't quietly remove a defense.

### Verification

```powershell
dotnet run --project D:\personal\Modulus\src\Modulus.Cli\Modulus.Cli.csproj -- init "Sample; rm -rf /" --output $env:TEMP
# Expect: validator rejects; no process spawn; no files.
```

---

## PR4 — Messaging Hardening (#6, #8, #9, #10)

### #8 first — Safe `GetTypes()` helper

New `src/Modulus.Messaging/Internals/AssemblyExtensions.cs`:

```csharp
internal static class AssemblyExtensions
{
    public static IReadOnlyList<Type> GetTypesSafe(this Assembly assembly)
    {
        if (assembly.IsDynamic) return [];
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }
    }
}
```

Apply at `OutboxProcessor.cs:26` and `ServiceCollectionExtensions.cs:115`.

### #6 Inbox registration

In `ServiceCollectionExtensions.cs` after `services.AddScoped<IOutboxStore, EfOutboxStore>()`:

```csharp
services.AddScoped<IInboxStore, EfInboxStore>();
```

Add symmetric extensions:

```csharp
public static IServiceCollection AddModulusInbox(this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
{
    services.AddDbContext<InboxDbContext>(configure);
    return services;
}

public static IServiceCollection AddModulusOutbox(this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
{
    services.AddDbContext<OutboxDbContext>(configure);
    return services;
}
```

XML doc on `AddModulusMessaging` spells out: call `AddModulusInbox` + run the schema migration to enable idempotency.

### #9 Retry policy + dead-letter

Extend `MessagingOptions`:

```csharp
public RetryPolicyOptions RetryPolicy { get; set; } = new();

public sealed class RetryPolicyOptions
{
    public int MaxAttempts { get; set; } = 5;
    public TimeSpan InitialInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan IntervalIncrement { get; set; } = TimeSpan.FromSeconds(5);
}
```

In `ServiceCollectionExtensions.cs:43-54`:

```csharp
busConfigurator.AddConsumer(adapterType)
    .Endpoint(e => e.UseMessageRetry(r => r.Exponential(
        options.RetryPolicy.MaxAttempts,
        options.RetryPolicy.InitialInterval,
        options.RetryPolicy.MaxInterval,
        options.RetryPolicy.IntervalIncrement)));
```

Outbox side: add `Attempts INT NOT NULL DEFAULT 0` and `LastError NVARCHAR(MAX) NULL` columns on `OutboxMessage`. Extend `IOutboxStore` with `MarkAsFailed(Guid messageId, string error, CancellationToken ct)`. After `RetryPolicy.MaxAttempts`, the processor stops re-publishing and logs `LogCritical`. Add composite indexes: `(ProcessedAt, CreatedAt)` on `OutboxMessage`, `(ProcessedOnUtc, OccurredOnUtc)` on `InboxMessage`.

### #10 DefaultAzureCredential path

Extend `MessagingOptions`:

```csharp
public string? FullyQualifiedNamespace { get; set; }
public Azure.Core.TokenCredential? Credential { get; set; }
```

In `ConfigureTransport` for `Transport.AzureServiceBus`:

```csharp
busConfigurator.UsingAzureServiceBus((context, cfg) =>
{
    if (options.Credential is not null)
    {
        if (string.IsNullOrWhiteSpace(options.FullyQualifiedNamespace))
            throw new InvalidOperationException(
                "FullyQualifiedNamespace is required when Credential is provided.");
        cfg.Host(options.FullyQualifiedNamespace, h => h.TokenCredential = options.Credential);
    }
    else
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException(
                "ConnectionString is required for Azure Service Bus when Credential is not set.");
        cfg.Host(options.ConnectionString);
    }
    cfg.ConfigureEndpoints(context);
});
```

### Tests

- `AssemblyExtensionsTests` — dynamic assembly returns empty; `ReflectionTypeLoadException` partial recovery.
- `InboxRegistrationTests` — `AddModulusMessaging + AddModulusInbox` → `IInboxStore` resolves; without `AddModulusInbox` the null-tolerant fallback still works.
- `RetryPolicyTests` — using `MassTransitTestHarness`: handler throws twice then succeeds → 3 attempts; always throws → message in DLQ after `MaxAttempts`.
- `AzureServiceBusOptionsTests` — credential without FQNS throws; credential with FQNS doesn't throw; null credential without connection string throws (regression).

---

## PR5 — Supply Chain Hygiene (#4 re-scoped)

### Pin MassTransit 7.3.1 with documented rationale

In `Directory.Packages.props`, add a top-of-file comment:

```xml
<!--
  MassTransit pinned to 7.3.1: v8 moved to a paid commercial license.
  We accept the EOL risk in exchange for OSS-only dependencies.
  CVE scanning runs in CI; if a critical advisory lands on v7, revisit the licensing trade.
-->
```

### CVE scanning in CI

Extend `.github/workflows/ci.yml`:

```yaml
- name: Check for vulnerable packages
  shell: pwsh
  run: |
    dotnet restore Modulus.slnx
    $output = dotnet list Modulus.slnx package --vulnerable --include-transitive 2>&1 | Out-String
    if ($output -match 'High|Critical') {
      Write-Error "Vulnerable packages detected:`n$output"
      exit 1
    }
```

Add a weekly scheduled run so a mid-week CVE surfaces within seven days:

```yaml
on:
  schedule:
    - cron: '0 6 * * 1'  # Mondays 06:00 UTC
```

### SourceLink + deterministic builds

In `Directory.Build.props`:

```xml
<PropertyGroup>
  <Deterministic>true</Deterministic>
  <ContinuousIntegrationBuild Condition="'$(GITHUB_ACTIONS)' == 'true'">true</ContinuousIntegrationBuild>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="All" />
</ItemGroup>
```

Add `Microsoft.SourceLink.GitHub` 8.0.0 to `Directory.Packages.props`.

### `SECURITY.md`

At repo root pointing to the maintainer email or GitHub private vulnerability reporting. One screen of text; satisfies the GitHub Security tab.

---

## Sequencing

```
PR1 + PR2  (Templates + UoW behavior — ship together)
PR3        (CLI hardening — independent)
PR4        (Messaging hardening — independent)
PR5        (Supply chain — land last so CVE scan runs against final deps)
```

PR1 and PR2 must ship together (or PR2 first) — the template after PR1 imports `UnitOfWorkBehavior` from `Modulus.Mediator.Behaviors`, which PR2 creates.

## End-to-End Verification

```powershell
$dir = Join-Path $env:TEMP "modulus-final-$(Get-Random)"
dotnet run --project D:\personal\Modulus\src\Modulus.Cli\Modulus.Cli.csproj -- init Sample --output $dir
dotnet run --project D:\personal\Modulus\src\Modulus.Cli\Modulus.Cli.csproj -- add module Catalog --solution (Join-Path $dir "Sample\Sample.slnx")
dotnet build (Join-Path $dir "Sample\Sample.slnx") /warnaserror /p:RunAnalyzersDuringBuild=true
dotnet test D:\personal\Modulus\Modulus.slnx
dotnet list D:\personal\Modulus\Modulus.slnx package --vulnerable --include-transitive
```

Expected end state: scaffold builds clean, Scalar exposed only in development, generated endpoint groups require authorization, exactly five pipeline behaviors registered, safe `ProcessRunner` shape, messaging with documented retry and DLQ.

## Files Touched by PR

| PR | Files |
|----|-------|
| PR1 | 6 template files, `ResourceManifest.cs`, `CLAUDE.md`, `src/Modulus.Mediator/README.md`, plus extensions to `InitHandlerTests.cs` and `AddModuleHandlerTests.cs` |
| PR2 | `src/Modulus.Mediator.Abstractions/Persistence/IUnitOfWork.cs`, `src/Modulus.Mediator/Behaviors/UnitOfWorkBehavior.cs`, `tests/Modulus.Mediator.Tests/Behaviors/UnitOfWorkBehaviorTests.cs` |
| PR3 | `IProcessRunner.cs`, `ProcessRunner.cs`, all six CLI handlers, new `PathGuard.cs`, `FakeProcessRunner`, plus tests |
| PR4 | `MessagingOptions.cs`, `ServiceCollectionExtensions.cs`, `OutboxProcessor.cs`, `EfOutboxStore.cs`, `OutboxDbContext.cs`, `InboxDbContext.cs`, new `AssemblyExtensions.cs`, plus tests |
| PR5 | `Directory.Build.props`, `Directory.Packages.props`, `.github/workflows/ci.yml`, new `SECURITY.md` |
