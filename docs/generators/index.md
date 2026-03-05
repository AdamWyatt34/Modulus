# Source Generators

Modulus includes three Roslyn incremental source generators that eliminate boilerplate and replace runtime reflection with compile-time code generation. Generators run during compilation and produce C# source files that are added to your project automatically.

## Why Source Generators?

Traditional approaches like Scrutor use runtime reflection to scan assemblies and register services. This works, but has drawbacks:

- **Startup cost** -- Assembly scanning happens on every application start
- **No AOT support** -- Reflection-based scanning is incompatible with Native AOT
- **Invisible errors** -- Missing registrations only surface at runtime as `null` or DI exceptions
- **Manual composition** -- Adding a module requires updating a registration file

Source generators solve all of these by producing explicit code at compile time. The generated code is visible, auditable, and runs with zero reflection overhead.

## Available Generators

| Generator | What it generates | What it replaces |
|---|---|---|
| [Strongly Typed IDs](./strongly-typed-ids) | Value type with EF Core, JSON, and TypeConverter support | Manual converter classes |
| [Handler Registration](./handler-registration) | `AddModulusHandlers()` extension method | Scrutor assembly scanning |
| [Module Auto-Discovery](./module-discovery) | `AddAllModules()` and `MapAllModuleEndpoints()` | Manual `ModuleRegistration.cs` |

## How They're Delivered

Both generators and analyzers are delivered as NuGet analyzer references. If you scaffolded your solution with the Modulus CLI, they are already configured. To add them manually:

```xml
<PackageReference Include="ModulusKit.Generators"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

Both `ModulusKit.Generators` and `ModulusKit.Analyzers` are also transitively included when you reference `ModulusKit.Mediator.Abstractions`.

## Generator Diagnostics

| ID | Severity | Description |
|---|---|---|
| MODGEN001 | Error | `[StronglyTypedId]` target must be `partial` |
| MODGEN002 | Error | `[StronglyTypedId]` target must be a `record struct` |
| MODGEN003 | Info | Open generic handler skipped for registration |
| MODGEN004 | Warning | `IModuleRegistration` implementation missing required static methods |

## Troubleshooting

### Generator not running

If generated code is not appearing:

1. **Rebuild the project** -- Incremental generators sometimes need a full rebuild after initial setup
2. **Check the target framework** -- Generators target `netstandard2.0`. Ensure your project references the correct package
3. **Verify the package reference** -- The generator must be referenced with `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`

### Types not found

If the `[StronglyTypedId]` or `[ModuleOrder]` attributes are not recognized:

1. Verify that `ModulusKit.Mediator.Abstractions` is referenced in your project
2. Check that the `using Modulus.Mediator.Abstractions;` directive is present

### Viewing generated code

To inspect the generated source files, add this to your `.csproj`:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Generated files will appear in `obj/Debug/net10.0/generated/`.

## How It Works

Modulus generators use the [Roslyn incremental generator](https://github.com/dotnet/roslyn/blob/main/docs/features/incremental-generators.md) model. Each generator:

1. **Registers a syntax provider** that filters for relevant syntax nodes (attributes, class declarations, etc.)
2. **Extracts metadata** from the semantic model (type names, interfaces, attributes)
3. **Produces source code** that is added to the compilation

The incremental model ensures generators only re-run when their inputs change, keeping IDE responsiveness high even in large solutions.

## What's Next

- **[Strongly Typed IDs](./strongly-typed-ids)** -- Generate type-safe entity identifiers
- **[Handler Registration](./handler-registration)** -- Auto-register handlers and validators
- **[Module Auto-Discovery](./module-discovery)** -- Eliminate manual module composition
