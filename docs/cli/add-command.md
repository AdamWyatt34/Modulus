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
| `--result-type, -r <type>` | Return type wrapped in `Result<T>`. Omit for a void `Result` (commands that return no value). | Void `Result` |

## Generated Output

Running `modulus add-command CreateProduct --module Catalog --result-type Guid` generates three files:

### Command record

`src/Modules/Catalog/EShop.Modules.Catalog.Application/Commands/CreateProduct/CreateProductCommand.cs`

```csharp
using EShop.SharedKernel.Application;

namespace EShop.Modules.Catalog.Application.Commands.CreateProduct;

public sealed record CreateProductCommand : ICommand<Guid>;
```

### Handler class

`src/Modules/Catalog/EShop.Modules.Catalog.Application/Commands/CreateProduct/CreateProductCommandHandler.cs`

```csharp
using EShop.SharedKernel.Application;

namespace EShop.Modules.Catalog.Application.Commands.CreateProduct;

public sealed class CreateProductCommandHandler
    : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Result<Guid>> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        // TODO: Implement command logic
        throw new NotImplementedException();
    }
}
```

### Validator class

`src/Modules/Catalog/EShop.Modules.Catalog.Application/Commands/CreateProduct/CreateProductCommandValidator.cs`

```csharp
using FluentValidation;

namespace EShop.Modules.Catalog.Application.Commands.CreateProduct;

public sealed class CreateProductCommandValidator
    : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator()
    {
        // TODO: Add validation rules
    }
}
```

When `--result-type` is omitted, the command implements `ICommand` (no generic parameter) and the handler returns `Result` instead of `Result<T>`.

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

## See Also

- [modulus add-query](./add-query) -- Scaffold read-side queries
- [modulus add-endpoint](./add-endpoint) -- Wire commands to HTTP endpoints
- [Commands & Queries](/mediator/commands-queries) -- How the mediator dispatches commands
- [Pipeline Behaviors](/mediator/pipeline-behaviors) -- Validation, logging, and other cross-cutting concerns
