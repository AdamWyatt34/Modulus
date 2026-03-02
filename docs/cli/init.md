# modulus init

Scaffolds a new modular monolith solution with all the foundational infrastructure in place. This is typically the first command you run when starting a new project.

## Synopsis

```bash
modulus init <solution-name> [options]
```

## Arguments

| Argument | Description |
|---|---|
| `<solution-name>` | PascalCase name for the solution. Used as the root namespace and directory name. |

## Options

| Option | Description | Default |
|---|---|---|
| `--output, -o <path>` | Output directory where the solution folder will be created | Current directory |
| `--aspire` | Include .NET Aspire AppHost and ServiceDefaults projects for service discovery, telemetry, and the developer dashboard | Not included |
| `--transport <transport>` | Messaging transport to configure: `inmemory`, `rabbitmq`, or `azureservicebus` | `inmemory` |
| `--no-git` | Skip `git init` and the initial commit | Git initialized |

## Generated Output

Running `modulus init EShop --aspire` generates the following structure:

```
EShop/
├── EShop.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── .gitignore
├── src/
│   ├── EShop.WebApi/
│   │   ├── EShop.WebApi.csproj
│   │   ├── Program.cs
│   │   ├── ModuleRegistration.cs
│   │   └── appsettings.json
│   ├── EShop.SharedKernel/
│   │   ├── EShop.SharedKernel.csproj
│   │   ├── Domain/
│   │   │   ├── Entity.cs
│   │   │   ├── AggregateRoot.cs
│   │   │   ├── StronglyTypedId.cs
│   │   │   ├── IDomainEvent.cs
│   │   │   └── IRepository.cs
│   │   ├── Application/
│   │   │   ├── ICommand.cs
│   │   │   ├── IQuery.cs
│   │   │   └── ICommandHandler.cs
│   │   ├── Infrastructure/
│   │   │   └── BaseDbContext.cs
│   │   └── Messaging/
│   │       ├── IIntegrationEvent.cs
│   │       └── IntegrationEventHandler.cs
│   ├── EShop.AppHost/              # only with --aspire
│   │   ├── EShop.AppHost.csproj
│   │   └── Program.cs
│   └── EShop.ServiceDefaults/      # only with --aspire
│       ├── EShop.ServiceDefaults.csproj
│       └── Extensions.cs
└── tests/
    └── EShop.ArchitectureTests/
        ├── EShop.ArchitectureTests.csproj
        └── ModuleBoundaryTests.cs
```

Key files:

- **`EShop.slnx`** -- The XML-based solution file that all modules will be added to.
- **`ModuleRegistration.cs`** -- The composition root where modules are registered. Updated automatically when you add modules.
- **`Directory.Packages.props`** -- Central package management so all projects share the same NuGet package versions.
- **`SharedKernel`** -- Common base types shared across all modules (entities, value objects, mediator abstractions).

## Examples

**Create a basic solution with in-memory transport:**

```bash
modulus init EShop
```

**Create a solution with Aspire support and RabbitMQ:**

```bash
modulus init EShop --aspire --transport rabbitmq
```

**Create a solution in a specific directory without git:**

```bash
modulus init EShop --output ~/projects --no-git
```

**Create a solution with Azure Service Bus:**

```bash
modulus init EShop --aspire --transport azureservicebus
```

## See Also

- [modulus add-module](./add-module) -- Add modules to your new solution
- [Getting Started: Your First Solution](/getting-started/first-solution) -- Step-by-step walkthrough
- [Architecture Overview](/architecture/) -- How the generated solution is structured
