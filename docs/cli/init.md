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
| `--modulus-kit-version <version>` | Override the `ModulusKit.*` package version emitted into `Directory.Packages.props` -- useful for pinning a known-good library set when the CLI and libraries were released at different versions | CLI's own version |

## Generated Output

Running `modulus init EShop --aspire` generates the following structure:

```
EShop/
├── EShop.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── .editorconfig
├── .gitignore
├── src/
│   ├── EShop.WebApi/
│   │   ├── EShop.WebApi.csproj
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   ├── Extensions/            # ConfigurationExtensions, ResultExtensions
│   │   ├── Middleware/            # GlobalExceptionHandler
│   │   └── Properties/launchSettings.json
│   ├── BuildingBlocks.Domain/
│   │   ├── Entities/              # Entity, AggregateRoot, IAuditable, IHasDomainEvents
│   │   ├── DomainEvents/          # IDomainEvent re-export, DomainEvent base record
│   │   ├── Identifiers/          # StronglyTypedId<T>
│   │   ├── ValueObjects/          # ValueObject
│   │   └── Exceptions/            # DomainException
│   ├── BuildingBlocks.Application/
│   │   ├── Persistence/           # IRepository<T, TId>
│   │   ├── Pagination/            # PaginationQuery, PagedResult<T>
│   │   └── DependencyInjection/   # AddApplicationServices
│   ├── BuildingBlocks.Infrastructure/
│   │   ├── Persistence/           # BaseDbContext, EfRepository, AuditableEntityInterceptor
│   │   ├── Endpoints/             # IEndpoint, ApiResults
│   │   ├── Registration/          # IModuleRegistration
│   │   ├── Outbox/                # Outbox EF configurations, IdempotentDomainEventHandler
│   │   └── Inbox/                 # Inbox EF configurations
│   └── BuildingBlocks.Integration/
│       └── IntegrationEvents/     # IIntegrationEvent re-export
├── aspire/                         # only with --aspire
│   ├── EShop.AppHost/
│   └── EShop.ServiceDefaults/
└── tests/
    ├── EShop.Tests.Common/
    ├── EShop.Tests.Architecture/
    └── EShop.Tests.Integration/
```

Key files:

- **`EShop.slnx`** -- The XML-based solution file that all modules will be added to.
- **`Program.cs`** -- The host's composition root. It calls the source-generated `AddModulusHandlers()`, `AddAllModules(builder.Configuration)`, and `MapAllModuleEndpoints()`; modules added later are picked up by the generator, so this file does not need editing per module.
- **`Directory.Packages.props`** -- Central package management so all projects share the same NuGet package versions; all `ModulusKit.*` packages are pinned to one version.
- **BuildingBlocks projects** -- Common base types shared across all modules (entities, value objects, endpoint plumbing, module registration contracts).

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
