# CLI Reference

The Modulus CLI is a .NET global tool that scaffolds production-ready modular monolith solutions, modules, and CQRS building blocks from the command line. It is built on [System.CommandLine](https://learn.microsoft.com/en-us/dotnet/standard/commandline/) and follows a predictable, convention-based workflow.

## Installation

```bash
dotnet tool install --global ModulusKit.Cli
```

To update an existing installation:

```bash
dotnet tool update --global ModulusKit.Cli
```

## Commands

| Command | Description |
|---|---|
| [`modulus init`](./init) | Create a new modular monolith solution |
| [`modulus add-module`](./add-module) | Add a feature module to an existing solution |
| [`modulus list-modules`](./list-modules) | List all modules in the current solution |
| [`modulus add-entity`](./add-entity) | Scaffold a domain entity or aggregate root |
| [`modulus add-command`](./add-command) | Scaffold a command, handler, and validator |
| [`modulus add-query`](./add-query) | Scaffold a query and handler |
| [`modulus add-endpoint`](./add-endpoint) | Scaffold a minimal API endpoint |
| [`modulus version`](./version) | Display the installed CLI version |

## Common Patterns

### Automatic Solution Discovery

Most commands need to know which solution they are operating on. Instead of requiring you to pass `--solution` every time, the CLI **auto-discovers** the nearest `.slnx` solution file by walking up the directory tree from your current working directory. This means you can run commands from any subdirectory inside your solution and the CLI will find the right solution file automatically.

If the auto-discovery finds the wrong file, or if you have multiple solutions in the same tree, you can always override with the `--solution` (or `-s`) flag:

```bash
modulus add-module Billing --solution ./path/to/MySolution.slnx
```

### PascalCase Naming

All names passed to the CLI -- solution names, module names, entity names, command names, query names, and endpoint names -- **must use PascalCase**. The CLI enforces this convention and will reject names that do not conform.

```bash
# Correct
modulus init EShop
modulus add-module OrderManagement
modulus add-entity ShoppingCart --module OrderManagement

# Incorrect - these will be rejected
modulus init e-shop
modulus add-module order_management
modulus add-entity shopping-cart --module OrderManagement
```

PascalCase naming ensures consistency across generated namespaces, class names, project files, and directory structures.

### Typical Workflow

A common workflow looks like this:

```bash
# 1. Create the solution
modulus init EShop --aspire --transport rabbitmq

# 2. Navigate into the solution directory
cd EShop

# 3. Add feature modules
modulus add-module Catalog
modulus add-module Orders
modulus add-module Identity

# 4. Add domain entities
modulus add-entity Product --module Catalog --aggregate --properties "Name:string,Price:decimal,Sku:string"
modulus add-entity Order --module Orders --aggregate --properties "CustomerId:Guid,Total:decimal"

# 5. Add CQRS components
modulus add-command CreateProduct --module Catalog --result-type Guid
modulus add-query GetProductById --module Catalog --result-type ProductDto
modulus add-command PlaceOrder --module Orders --result-type Guid

# 6. Wire up API endpoints
modulus add-endpoint CreateProduct --module Catalog --method POST --route / --command CreateProduct --result-type Guid
modulus add-endpoint GetProduct --module Catalog --method GET --route "/{id:guid}" --query GetProductById --result-type ProductDto

# 7. Run it
dotnet run --project src/EShop.WebApi
```
