# modulus add-command

Scaffolds a CQRS command with its handler and FluentValidation validator inside a module's Application layer. Commands represent intent to change state and are dispatched through the Modulus mediator pipeline.

## Synopsis

```bash
modulus add-command <command-name> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `<command-name>` | PascalCase name for the command (e.g., `CreateProduct`, `PlaceOrder`). |

## Options

| Option | Description | Default |
|---|---|---|
| `--module, -m <name>` | **(Required)** Target module where the command will be created. | -- |
| `--solution, -s <path>` | Path to the `.slnx` solution file. | Auto-discovered |
| `--result-type, -r <type>` | Return type wrapped in `Result<T>`. Omit for a void `Result` (commands that return no value). Accepts built-in aliases, BCL types, fully-qualified names, nullable (`Guid?`), arrays (`int[]`, `int[,]`), and generic types (`List<Guid>`, `Dictionary<string, decimal>`, arbitrarily nested). | Void `Result` |
| `--dry-run` | Print the files that would be created without writing anything. | Disabled |

## Generated Output

Running `modulus add-command CreateProduct --module Catalog --result-type Guid` generates four files -- the command record, handler, and validator under `src/Catalog.Application/Commands/CreateProduct/`, plus a starter unit test. The record and classes are named exactly after the command (no `Command` suffix is appended):

### Command record

`src/Modules/Catalog/src/Catalog.Application/Commands/CreateProduct/CreateProduct.cs`

```csharp
using Modulus.Mediator.Abstractions;

namespace EShop.Catalog.Application.Commands.CreateProduct;

public sealed record CreateProduct : ICommand<Guid>;
```

### Handler class

`src/Modules/Catalog/src/Catalog.Application/Commands/CreateProduct/CreateProductHandler.cs`

```csharp
using Modulus.Mediator.Abstractions;

namespace EShop.Catalog.Application.Commands.CreateProduct;

public sealed class CreateProductHandler : ICommandHandler<CreateProduct, Guid>
{
    public Task<Result<Guid>> Handle(CreateProduct command, CancellationToken cancellationToken = default)
    {
        // TODO: Implement command logic
        throw new NotImplementedException();
    }
}
```

### Validator class

`src/Modules/Catalog/src/Catalog.Application/Commands/CreateProduct/CreateProductValidator.cs`

```csharp
using FluentValidation;

namespace EShop.Catalog.Application.Commands.CreateProduct;

public sealed class CreateProductValidator : AbstractValidator<CreateProduct>
{
    public CreateProductValidator()
    {
        // TODO: Add validation rules
    }
}
```

### Unit test

`src/Modules/Catalog/tests/Catalog.Tests.Unit/Commands/CreateProductHandlerTests.cs` -- a starter test that constructs the handler directly. Both the handler and the validator are auto-registered by the source-generated `AddModulusHandlers()`.

When `--result-type` is omitted, the command implements `ICommand` (no generic parameter) and the handler returns `Result` instead of `Result<T>` (with a `Result.Success()` placeholder body instead of `NotImplementedException`).

## Examples

**Create a command that returns a Guid:**

```bash
modulus add-command CreateProduct --module Catalog --result-type Guid
```

**Create a void command (no return value):**

```bash
modulus add-command DeleteProduct --module Catalog
```

**Create a command that returns a custom type:**

```bash
modulus add-command PlaceOrder --module Orders --result-type OrderConfirmation
```

**Create a command that returns a generic collection type:**

```bash
modulus add-command ImportProducts --module Catalog --result-type "List<Guid>"
```

**Preview the files that would be created without writing anything:**

```bash
modulus add-command CreateProduct --module Catalog --result-type Guid --dry-run
```

## See Also

- [modulus add-query](./add-query) -- Scaffold read-side queries
- [modulus add-endpoint](./add-endpoint) -- Wire commands to HTTP endpoints
- [Commands & Queries](/mediator/commands-queries) -- How the mediator dispatches commands
- [Pipeline Behaviors](/mediator/pipeline-behaviors) -- Validation, logging, and other cross-cutting concerns
