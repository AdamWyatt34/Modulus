# Aspire Integration

.NET Aspire is a cloud-ready stack for building observable, production-ready distributed applications. Modulus integrates with Aspire out of the box when you initialize a solution with the `--aspire` flag, giving you a developer dashboard, distributed telemetry, health checks, and service discovery with zero manual setup.

## What .NET Aspire Provides

| Capability | Description |
|---|---|
| **Developer Dashboard** | Real-time view of logs, traces, metrics, and environment variables for all projects |
| **Distributed Tracing** | End-to-end OpenTelemetry traces across HTTP calls, database queries, and message bus operations |
| **Structured Logging** | Centralized log aggregation with filtering and search |
| **Metrics** | Built-in .NET runtime and ASP.NET Core metrics, plus custom application metrics |
| **Health Checks** | Automatic health check endpoints with liveness and readiness probes |
| **Service Discovery** | Named service references resolved at runtime without hardcoded URLs |
| **Resilience** | Pre-configured HTTP resilience policies (retries, circuit breakers, timeouts) |

## Using the --aspire Flag

To scaffold a solution with Aspire integration:

```bash
modulus init EShop --aspire
```

You can combine `--aspire` with other flags:

```bash
modulus init EShop --aspire --transport rabbitmq
```

::: tip Aspire is optional
If you omit the `--aspire` flag, the solution is generated without the AppHost and ServiceDefaults projects. You can add Aspire support later by creating these projects manually.
:::

## Generated Projects

The `--aspire` flag adds two projects to the solution:

```
src/
├── EShop.AppHost/           # Aspire orchestrator
├── EShop.ServiceDefaults/   # Shared configuration
└── EShop.WebApi/            # Your host application
```

### AppHost (Orchestrator)

The AppHost project is the entry point when running with Aspire. It references all projects in the solution and orchestrates their startup. It is **not** deployed to production -- it is a development-time tool.

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.EShop_WebApi>("webapi");

builder.Build().Run();
```

The AppHost is a `DistributedApplication` that:

1. Starts all referenced projects as child processes.
2. Injects service discovery configuration so projects can find each other by name.
3. Collects telemetry (logs, traces, metrics) from all projects and surfaces them in the dashboard.
4. Runs health checks and displays their status.

### ServiceDefaults (Shared Configuration)

The ServiceDefaults project is a shared library referenced by all application projects (e.g., `EShop.WebApi`). It configures OpenTelemetry, HTTP resilience, service discovery, and health checks in a single place.

```csharp
// In EShop.WebApi/Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// ... register modules, mediator, messaging ...

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
```

The `AddServiceDefaults()` extension method registers:

- **OpenTelemetry** -- Tracing and metrics exporters that send data to the Aspire dashboard.
- **HTTP Resilience** -- Default retry, circuit breaker, and timeout policies for `HttpClient`.
- **Service Discovery** -- Resolution of named services (e.g., `https+http://catalog`) to actual URLs.
- **Health Checks** -- Liveness and readiness check registrations.

The `MapDefaultEndpoints()` extension method maps the health check HTTP endpoints.

## Running with Aspire

Start the solution through the AppHost project:

```bash
dotnet run --project src/EShop.AppHost
```

This starts:

1. The Aspire dashboard on `http://localhost:15888`.
2. The `EShop.WebApi` application (port assigned dynamically or configured in `launchSettings.json`).
3. Any infrastructure resources defined in the AppHost (databases, message brokers, etc.).

::: info Dashboard authentication
The Aspire dashboard generates a one-time login token on startup. Look for the token in the console output:

```
Login to the dashboard at http://localhost:15888/login?t=<token>
```
:::

## Aspire Dashboard

The dashboard provides a unified view of your entire application:

- **Projects** -- Status, endpoints, and environment variables for each project.
- **Logs** -- Structured log entries from all projects, filterable by severity and source.
- **Traces** -- Distributed traces showing the full call chain across HTTP, database, and messaging operations.
- **Metrics** -- Runtime metrics (GC, thread pool), ASP.NET Core metrics (request rate, latency), and custom counters.

## Adding Infrastructure Resources

The AppHost can provision and manage infrastructure resources that your application depends on. Resources are configured in the AppHost's `Program.cs` and automatically injected into the projects that reference them.

### PostgreSQL Database

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("eshopdb");

builder.AddProject<Projects.EShop_WebApi>("webapi")
    .WithReference(postgres);

builder.Build().Run();
```

The connection string is automatically injected into `EShop.WebApi` as the `ConnectionStrings:eshopdb` configuration value. Your existing `DbContext` configuration picks it up without changes.

### RabbitMQ Message Broker

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("messaging");

builder.AddProject<Projects.EShop_WebApi>("webapi")
    .WithReference(rabbitmq);

builder.Build().Run();
```

### Redis Cache

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("cache");

builder.AddProject<Projects.EShop_WebApi>("webapi")
    .WithReference(redis);

builder.Build().Run();
```

### Combined Example

A full AppHost with database, messaging, and caching:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("eshopdb");

var rabbitmq = builder.AddRabbitMQ("messaging");
var redis = builder.AddRedis("cache");

builder.AddProject<Projects.EShop_WebApi>("webapi")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithReference(redis);

builder.Build().Run();
```

::: warning Docker required for infrastructure resources
Aspire runs infrastructure resources (PostgreSQL, RabbitMQ, Redis) as Docker containers. You need Docker Desktop or a compatible container runtime installed and running.
:::

## ServiceDefaults Packages

The ServiceDefaults project includes these key NuGet packages:

| Package | Purpose |
|---|---|
| `Microsoft.Extensions.Http.Resilience` | Retry, circuit breaker, and timeout policies for `HttpClient` |
| `Microsoft.Extensions.ServiceDiscovery` | Named service resolution (e.g., `https+http://catalog`) |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTLP exporter for traces and metrics |
| `OpenTelemetry.Extensions.Hosting` | OpenTelemetry host integration |
| `OpenTelemetry.Instrumentation.AspNetCore` | Automatic trace instrumentation for ASP.NET Core |
| `OpenTelemetry.Instrumentation.Http` | Automatic trace instrumentation for outgoing HTTP calls |
| `OpenTelemetry.Instrumentation.Runtime` | .NET runtime metrics (GC, thread pool, etc.) |

## Health Check Endpoints

The `MapDefaultEndpoints()` method registers two health check endpoints:

| Endpoint | Purpose | Typical Use |
|---|---|---|
| `/healthz` | **Liveness probe** -- Returns 200 if the process is alive | Kubernetes liveness probe, load balancer health check |
| `/readyz` | **Readiness probe** -- Returns 200 if the application is ready to serve traffic | Kubernetes readiness probe, deployment gate |

Both endpoints return a simple `Healthy` / `Unhealthy` status by default. You can register additional health checks (database connectivity, message broker reachability, etc.) in your module registration:

```csharp
public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddHealthChecks()
        .AddNpgSql(configuration.GetConnectionString("Database")!, name: "catalog-db")
        .AddRabbitMQ(name: "messaging");
}
```

The readiness probe aggregates all registered checks, so it only reports `Healthy` when all dependencies are reachable.

## Extracting Services with Aspire

When you [extract a module into its own service](/architecture/extraction), Aspire makes the transition straightforward. Add the new service project to the AppHost and Aspire handles service discovery, telemetry aggregation, and health monitoring automatically:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .AddDatabase("eshopdb");

var rabbitmq = builder.AddRabbitMQ("messaging");

var catalogService = builder.AddProject<Projects.EShop_Catalog_WebApi>("catalog")
    .WithReference(postgres)
    .WithReference(rabbitmq);

var monolith = builder.AddProject<Projects.EShop_WebApi>("webapi")
    .WithReference(postgres)
    .WithReference(rabbitmq)
    .WithReference(catalogService);  // service discovery

builder.Build().Run();
```

The monolith can now discover the Catalog service by name (`https+http://catalog`) without hardcoding URLs. The dashboard shows traces that span both services, making it easy to debug cross-service interactions.

## See Also

- [Architecture Overview](/architecture/) -- Modular monolith structure and module isolation
- [Extracting to Microservices](/architecture/extraction) -- Breaking modules out of the monolith
- [Health Checks](/recipes/health-checks) -- Custom health check implementations
- [Your First Solution](/getting-started/first-solution) -- End-to-end walkthrough with Aspire
