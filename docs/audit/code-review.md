# Modulus — Code Review (2026-05-17)

Full review of the Modulus library ecosystem (`ModulusKit.*`): seven NuGet packages for scaffolding .NET 10 modular monoliths — custom CQRS mediator with pipeline behaviors, MassTransit-based messaging with outbox/inbox, Roslyn source generators and analyzers, and the `modulus` dotnet tool that emits 117 embedded templates.

Findings are tagged `[CRITICAL] / [HIGH] / [MED] / [LOW] / [NIT] / [INFO]` with file paths and line numbers. Severity reflects impact on consumers, not effort to fix.

See [`top-10-fixes-plan.md`](top-10-fixes-plan.md) for the sequenced PR plan that addresses the highest-impact items, and [`missing-features-plan.md`](missing-features-plan.md) for the tiered backlog of nice-to-haves.

---

## Top Issues (fix before next release)

| # | Severity | Finding | File(s) |
|---|----------|---------|---------|
| 1 | HIGH | **Generated `Program.cs` exposes OpenAPI + Scalar in production unconditionally** — no `IsDevelopment()` guard. | `src/Modulus.Templates/templates/init/host/Program.cs.template:30-31` |
| 2 | HIGH | **Scaffolded solution has no authentication or authorization wired in.** No `AddAuthentication` / `AddAuthorization`, no `.RequireAuthorization()` on the sample endpoint. | `src/Modulus.Templates/templates/init/host/Program.cs.template`, `src/Modulus.Templates/templates/module/src/ModuleName.Api/Endpoints/GetSample.cs.template:13-18` |
| 3 | HIGH (re-scoped) | **Template ships its own `UnitOfWorkBehavior<,>` and `IUnitOfWork`** inside the scaffold. Original audit claim "library doesn't ship it → won't compile" was a false positive; the template-shipped copies make it compile. The real fix is to consolidate on a library-shipped behavior so every consumer gets the same implementation. | `src/Modulus.Templates/templates/init/building-blocks/application/IUnitOfWork.cs.template`, `.../Behaviors/UnitOfWorkBehavior.cs.template` |
| 4 | HIGH | **MassTransit 7.3.1 is end-of-support** — v8 moved to a paid commercial license. Codebase uses v7-only namespace `MassTransit.ExtensionsDependencyInjectionIntegration`. Per project decision, stay on v7 and harden (pin + CVE scan in CI) rather than upgrade. | `Directory.Packages.props:18-21`, `src/Modulus.Messaging/DependencyInjection/ServiceCollectionExtensions.cs:3` |
| 5 | HIGH | **No path-containment check on file writes.** Every CLI handler does `Path.Combine(solutionRoot, output.RelativePath)` without verifying the canonical result stays inside `solutionRoot`. | `src/Modulus.Cli/Handlers/InitHandler.cs:57`, `AddModuleHandler.cs:92`, `AddCommandHandler.cs:82`, `AddQueryHandler.cs:82`, `AddEntityHandler.cs:95`, `AddEndpointHandler.cs:121` |
| 6 | HIGH | **`IInboxStore` is never registered by `AddModulusMessaging()`.** `IdempotentConsumerAdapter` resolves via `GetService<IInboxStore>()` and silently no-ops if null — idempotency is off by default unless the consumer wires it manually. | `src/Modulus.Messaging/DependencyInjection/ServiceCollectionExtensions.cs:56-58` |
| 7 | HIGH | **`ProcessRunner` uses single interpolated `Arguments` string instead of `ArgumentList`.** Today safe (`CSharpIdentifierValidator` rejects metacharacters); architecture is one validator relaxation away from quoting injection. | `src/Modulus.Cli/Infrastructure/ProcessRunner.cs:12`, `src/Modulus.Cli/Handlers/AddModuleHandler.cs:140` |
| 8 | HIGH | **`assembly.GetTypes()` called unguarded** — crashes startup on `ReflectionTypeLoadException` or dynamic assemblies. | `src/Modulus.Messaging/DependencyInjection/ServiceCollectionExtensions.cs:115`, `src/Modulus.Messaging/Outbox/OutboxProcessor.cs:26` |
| 9 | HIGH | **No MassTransit consumer retry / dead-letter policy.** `busConfigurator.AddConsumer(adapterType)` has no `.UseMessageRetry()`. A transient DB timeout faults the message permanently. | `src/Modulus.Messaging/DependencyInjection/ServiceCollectionExtensions.cs:43-54` |
| 10 | HIGH | **No `TokenCredential` / `DefaultAzureCredential` path for Azure Service Bus.** Connection strings are the only option, forcing every Azure consumer to store a SAS string instead of using managed identity. | `src/Modulus.Messaging/DependencyInjection/ServiceCollectionExtensions.cs:88-96` |

---

## Architecture & Public API

### Mediator (`src/Modulus.Mediator`)

- **[HIGH]** No `ConfigureAwait(false)` anywhere in `src/Modulus.Mediator/` — every `await` in `Mediator.cs` and the four behavior classes. Library code; consumers may run with a sync context. Messaging *does* use it, making the gap conspicuous. Affects `Mediator.cs:115,141,155,169,203` and all four behaviors.
- **[MED]** `AddPipelineBehavior(this IServiceCollection, Type behaviorType)` (`ServiceCollectionExtensions.cs:23`) takes an untyped `Type` — wrong type fails at runtime resolution. Add a generic overload `AddPipelineBehavior<TBehavior>()` and an `ArgumentException` guard.
- **[MED]** **Pipeline behavior order documented three different ways**:
  - `CLAUDE.md` — `UnhandledException → Logging → Validation → Metrics`
  - `src/Modulus.Mediator/README.md:17-19` — `UnhandledException → Logging → Validation`
  - Scaffolded template — `UnhandledException → Logging → Metrics → Validation → UnitOfWork`
- **[MED]** `Mediator.cs:50-51` — `ConcurrentDictionary` keys on `commandType` only but the cached `MethodInfo` is closed over `TResult`. Safe today (interface constraint), but key on `(commandType, typeof(TResult))` to make the invariant explicit.
- **[MED]** `Mediator.cs:172-182` — `StreamInternal` doesn't call `cancellationToken.ThrowIfCancellationRequested()` before invoking the handler.
- **[MED]** `Mediator.cs:12-21` — four static `MethodInfo` fields use `!` null-forgiving. Rename refactor → silent NRE at first dispatch. Replace with `?? throw new InvalidOperationException(...)`.
- **[MED]** `Result` / `Result<TValue>` (`Result.cs`, `ResultT.cs`) are non-sealed because `ValidationResult` inherits. Either document the inheritance contract or restrict it (internal constructors).
- **[LOW]** `IStreamQuery<TResult>` is a marker interface with an unused type parameter — forces consumers to repeat the type at every call.

### Messaging (`src/Modulus.Messaging`)

- **[HIGH]** **`MassTransitMessageBus.Send` hard-codes endpoint URI** as `queue:{typeof(TCommand).Name}` (`MassTransitMessageBus.cs:17`). Bypasses the configured endpoint name formatter (kebab-case in MT v8). Use the registered endpoint conventions.
- **[HIGH]** **`Modulus.Messaging.csproj` has MassTransit RabbitMQ + Azure Service Bus + EF Core as required transitive dependencies.** A consumer using only the InMemory transport drags all of it in. Split into `ModulusKit.Messaging`, `ModulusKit.Messaging.RabbitMq`, `ModulusKit.Messaging.AzureServiceBus`.
- **[MED]** `EfInboxStore` `AnyAsync` + `Add` has a TOCTOU window protected only by the `DbUpdateException` fallback catch. Document the pre-check as a performance optimization, not a correctness guarantee.
- **[MED]** `InboxDbContext` is `public class` while `OutboxDbContext` is `public sealed`. Pick one.
- **[LOW]** `OutboxDbContext` has no index on `(ProcessedAt, CreatedAt)`. `InboxDbContext` has no index on `(ProcessedOnUtc, OccurredOnUtc)`. Both polling queries do full scans.
- **[LOW]** Inbox dedup key is producer-assigned `EventId` (`IdempotentConsumerAdapter.cs:29`) — spoofable by a colluding producer. Document the trust assumption.

### Source Generators (`src/Modulus.Generators`)

- **[HIGH]** **`HandlerRegistrationGenerator.FindHandlersInReferencedAssemblies`** traverses every symbol in every referenced assembly on every `Compilation` change. Re-runs on every keystroke in the IDE. Will tank perf in large solutions. Gate on `MetadataReference` changes or scan only assemblies whose name matches a marker.
- **[HIGH]** **No `[System.CodeDom.Compiler.GeneratedCode(...)]` attribute on generated classes.** Only `// <auto-generated/>` is emitted. StyleCop/Sonar rely on the attribute. Add to `HandlerRegistrationGenerator.cs:264-307`, `ModuleRegistrationGenerator.cs:217-268`, `StronglyTypedIdGenerator.cs`.
- **[MED]** `HandlerRegistrationGenerator.AddRange` (`line 51`) doesn't dedupe local + referenced-assembly results — handlers from referenced packages register twice.
- **[MED]** `GetOpenGenericDiagnostic` message says what's wrong but not how to fix it.
- **[LOW]** `StronglyTypedIdGenerator.cs:148` emits the nested `ValueConverter` opening brace at column 0 instead of `"    {"`. Cosmetic only — braces balance so compilation succeeds. Neighboring `JsonConverter` (`:162-169`) does it correctly.
- **[LOW]** `ModuleRegistrationGenerator.ImplementsIModuleRegistration` matches by namespace suffix — breaks on rename. Accept a `[ModulusModule]` attribute as an alternative discovery path.
- **[LOW]** `HandlerRegistrationGenerator.IsCandidate` filters `ClassDeclarationSyntax` only — `record class` handlers silently ignored.

### Analyzers (`src/Modulus.Analyzers`)

- All five MOD001-MOD005 rules implemented with appropriate severities. Self-compliance is good (library's handlers return `Result`; `Mediator` throws only `InvalidOperationException` for configuration errors, which `ExceptionThrowingInHandlerAnalyzer.cs:25` excludes).
- **[MED]** `OutboxMessage.cs:21` and `InboxMessage` use public `set;` on `ProcessedAt` / `ProcessedOnUtc` while peers use `init`. If MOD005 scans these, they trip the library's own rule. Switch to `internal set` + `InternalsVisibleTo`.

### Templates (`src/Modulus.Templates/templates/`)

Beyond the HIGH security findings:

- **[MED]** `appsettings.json.template:3` — `TrustServerCertificate=true` propagates into staging/prod copies. Comment or move to dev-only file.
- **[MED]** `appsettings.json.template:12` — `"AllowedHosts": "*"`. Narrow or comment.
- **[MED]** `EfRepository.cs.template`, `OutboxConfiguration.cs.template`, `IdempotentDomainEventHandler.cs.template`, `BaseDbContext.cs.template` — non-`sealed` despite project convention. Sample handler lacks a primary constructor.
- **[MED]** `OutboxMessageConsumer.cs.template` uses public `get; set;` — MOD005 trips the moment the user opens the project.
- **[MED]** `IdempotentDomainEventHandler.cs.template:22-25` — check-then-insert with no unique index = TOCTOU.
- **[LOW]** `GetSample.cs.template` maps `app.MapGet("/", ...)` — literal `/` collides with the module group root.

### CLI Handlers (`src/Modulus.Cli/Handlers`)

- **[HIGH]** **No cleanup on partial failure.** `InitHandler.cs:47-65` and `AddModuleHandler.cs:89-103` write in a `foreach` loop; mid-loop failure leaves partial state and re-runs report "already exists".
- **[MED]** `InitHandler.cs:82-83` — `git add` and `git commit` are sequential without checking the first's exit code.
- **[MED]** `AddModuleHandler.StripApiReferencesFromArchTests` / `StripApiReferencesFromModuleClass` mutate generated content with line-and-brace counting (`AddModuleHandler.cs:150-207`). Fragile to template formatting changes. Roslyn-rewrite or ship two template variants.
- **[MED]** `ProcessRunner.RunAsync` doesn't accept `CancellationToken`. `WaitForExitAsync()` on line 27 has no token.
- **[MED]** `InitCommand` validates `--transport` imperatively in `SetAction` instead of using `Option<T>.parseArgument`.

---

## .NET Language Quality

- **[HIGH]** `ResultFactory` (`src/Modulus.Mediator/Internals/ResultFactory.cs:16-21,38-41`) does `GetMethod(...) + Invoke` on every validation failure and every unhandled exception. Cache the `MethodInfo` per `TResponse`.
- **[MED]** `LoggingBehavior.cs:34` computes `string.Join(...)` unconditionally but only uses it in the `else` branch.
- **[MED]** `HandlerRegistration.GetHashCode` (`HandlerRegistrationGenerator.cs:374-380`) null-coalesces on never-null properties; if a property were null, `?? 0` collapses hash space.
- **[MED]** `Result.Failure(IEnumerable<Error>)` does `.ToArray()` on what's often already an array.
- **[LOW]** `ModuleRegistrationGenerator.cs:84` `fqn.Substring("global::".Length)` → range indexer `fqn["global::".Length..]`.
- **[LOW]** `GetCategoryComment` (`HandlerRegistrationGenerator.cs:310-321`) classic `switch` → switch expression.
- **[LOW]** `OutboxProcessor._allowedTypes` could be `FrozenDictionary<string, Type>` for faster reads and clearer intent.
- **[NIT]** `MassTransitMessageBus.Publish` returns `bus.Publish(...)` without `await`. An earlier review flagged this as a bug; it's actually correct — returning the inner `Task` skips a state machine and `ConfigureAwait` doesn't apply when you don't `await`.

---

## Security (beyond Top 10)

- **[INFO]** **`String.Replace` token substitution is safe today** because `CSharpIdentifierValidator` rejects all context-breaking characters (`<`, `>`, `&`, `"`, `'`, `;`, `{`, `}`, `:`, `(`, `)`, `.`, space). The safety is implicit. Document the contract on the validator class so a future contributor doesn't accidentally remove a defense by loosening the rule.
- **[MED]** No SourceLink, `Deterministic`, or `EmbedUntrackedSources` in `Directory.Build.props`. Add `Microsoft.SourceLink.GitHub` package + the three properties for reproducible builds and forensic traceability.
- **[MED]** No NuGet package signing.
- **[LOW]** No `SECURITY.md` at the repo root.
- **[INFO]** Source generator scanning uses Roslyn semantic model only — no `Assembly.LoadFrom`, no attribute construction at generation time. Clean.
- **[INFO]** No connection strings logged. Outbox/inbox use a `Dictionary<string, Type>` allowlist before `JsonSerializer.Deserialize`. Clean.

---

## Summary

The codebase is well-architected with clear pillar separation (Mediator / Messaging / CLI). The Abstractions packages are correctly dependency-free. The most actionable issues are:

1. **Template security defaults** — Scalar exposed in production and no auth scaffolding in every generated solution.
2. **Messaging hardening** — Inbox not registered, no retry / DLQ, no managed-identity path.
3. **CLI safety** — Path containment missing on every write site; `ProcessRunner` uses interpolation instead of `ArgumentList`.
4. **Supply chain** — MassTransit v7 is EOL but staying for licensing reasons; needs documented rationale + CVE scanning.
5. **Generator output quality** — Missing `[GeneratedCode]` attribute, no dedupe across local + referenced handlers, and the referenced-assembly scan re-runs on every keystroke.

See [`top-10-fixes-plan.md`](top-10-fixes-plan.md) for the sequenced implementation plan addressing these items across five PRs.
