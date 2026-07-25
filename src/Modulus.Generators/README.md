# ModulusKit.Generators

Roslyn incremental source generators for Modulus -- strongly typed IDs, handler registration, and module auto-discovery.

## Installation

```bash
dotnet add package ModulusKit.Generators
```

Or as an analyzer reference in your `.csproj`:

```xml
<PackageReference Include="ModulusKit.Generators"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

> **Note:** This is a development-dependency (analyzer) package -- it does **not** flow transitively through other `ModulusKit.*` packages or through project references. Add the reference above to every project that defines handlers, validators, or `[StronglyTypedId]` types (and to the host, for module discovery). Solutions scaffolded by the `modulus` CLI have this wired up already.

## Strongly Typed IDs

Generate type-safe entity identifiers with EF Core, JSON, and model binding support:

```csharp
using Modulus.Mediator.Abstractions;

[StronglyTypedId]
public readonly partial record struct OrderId;

[StronglyTypedId(typeof(int))]
public readonly partial record struct SequenceNumber;
```

The generator produces: `Value` property, constructor, `New()` factory, `Empty`, plus `ValueConverter` (EF Core), `JsonConverter` (System.Text.Json), and `TypeConverter` (model binding).

Supported backing types: `Guid` (default), `int`, `long`.

## Handler Registration

Auto-register all handlers and validators at compile time:

```csharp
// Source-generated extension method
services.AddModulusHandlers();
```

Discovers: `ICommandHandler<>`, `IQueryHandler<>`, `IStreamQueryHandler<>`, `IDomainEventHandler<>`, `IIntegrationEventHandler<>`, and `AbstractValidator<>`.

## Module Auto-Discovery

Auto-discover all `IModuleRegistration` implementations from referenced assemblies:

```csharp
// Source-generated extension methods
builder.Services.AddAllModules(builder.Configuration);
app.MapAllModuleEndpoints();
```

Control initialization order with `[ModuleOrder(n)]`.

## Diagnostics

| ID | Severity | Description |
|---|---|---|
| MODGEN001 | Error | `[StronglyTypedId]` target must be `partial` |
| MODGEN002 | Error | `[StronglyTypedId]` target must be a `record struct` |
| MODGEN003 | Info | Open generic handler skipped for registration |
| MODGEN004 | Warning | `IModuleRegistration` missing required static methods |
| MODGEN005 | Error | `[StronglyTypedId]` backing type is unsupported (only `Guid`, `int`, `long`) |
| MODGEN006 | Error | `[StronglyTypedId]` target must be a top-level type |

## Learn More

See the [Modulus documentation](https://adamwyatt34.github.io/Modulus/generators/) for full generator reference.
