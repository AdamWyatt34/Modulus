# Health Checks

## Problem

In production, you need a way to monitor whether your application and its dependencies are healthy. Load balancers need a liveness endpoint to know if the process is running. Orchestrators like Kubernetes need a readiness endpoint to know if the application can serve traffic. Operations teams need visibility into the health of individual modules and their dependencies.

## Solution

Use ASP.NET Core health checks to expose `/healthz` (liveness) and `/readyz` (readiness) endpoints. Add per-module health checks for database connectivity and message broker connections. If your solution uses Aspire ServiceDefaults, many of these are preconfigured for you.

### Health Checks with Aspire ServiceDefaults

If you scaffolded your solution with Aspire support (`modulus init --aspire`), the ServiceDefaults project already configures health check endpoints:

```csharp
// Automatically provided by ServiceDefaults
app.MapDefaultEndpoints();
// Exposes:
//   /healthz  -- liveness (always returns Healthy if the process is running)
//   /readyz   -- readiness (checks all registered health checks)
```

You can add module-specific health checks on top of the Aspire defaults.

### Step 1: Install Health Check Packages

If you are not using Aspire ServiceDefaults, or want to add database-specific checks:

```bash
dotnet add src/EShop.Host/ package AspNetCore.HealthChecks.NpgSql
dotnet add src/EShop.Host/ package AspNetCore.HealthChecks.Rabbitmq
```

### Step 2: Configure Health Checks in the Host

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    // Database checks
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Database")!,
        name: "postgresql",
        tags: ["db", "ready"])
    // Message broker check
    .AddRabbitMQ(
        builder.Configuration.GetConnectionString("RabbitMQ")!,
        name: "rabbitmq",
        tags: ["messaging", "ready"]);

var app = builder.Build();

// Liveness -- returns Healthy if the process is running
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false  // no checks -- just confirms the process is alive
});

// Readiness -- runs all checks tagged with "ready"
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteDetailedResponse
});

app.Run();
```

### Step 3: Custom Response Writer

The default health check response is a simple `Healthy` / `Unhealthy` string. For production monitoring, you want a detailed JSON response:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

static Task WriteDetailedResponse(
    HttpContext context,
    HealthReport report)
{
    context.Response.ContentType = "application/json";

    var response = new
    {
        status = report.Status.ToString(),
        duration = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            duration = entry.Value.Duration.TotalMilliseconds,
            description = entry.Value.Description,
            exception = entry.Value.Exception?.Message,
            data = entry.Value.Data
        })
    };

    return context.Response.WriteAsJsonAsync(response,
        new JsonSerializerOptions { WriteIndented = true });
}
```

Example response:

```json
{
  "status": "Healthy",
  "duration": 45.2,
  "checks": [
    {
      "name": "postgresql",
      "status": "Healthy",
      "duration": 12.1,
      "description": null,
      "exception": null,
      "data": {}
    },
    {
      "name": "rabbitmq",
      "status": "Healthy",
      "duration": 8.7,
      "description": null,
      "exception": null,
      "data": {}
    }
  ]
}
```

### Step 4: Per-Module Health Checks

Create custom health checks for module-specific concerns. For example, verify that a module's database migrations are up to date:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.Modules.Catalog.Infrastructure;

public class CatalogDatabaseHealthCheck : IHealthCheck
{
    private readonly CatalogDbContext _dbContext;

    public CatalogDatabaseHealthCheck(CatalogDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify the database is accessible
            await _dbContext.Database.CanConnectAsync(cancellationToken);

            // Verify pending migrations
            var pending = await _dbContext.Database
                .GetPendingMigrationsAsync(cancellationToken);

            if (pending.Any())
            {
                return HealthCheckResult.Degraded(
                    $"There are {pending.Count()} pending migrations.",
                    data: new Dictionary<string, object>
                    {
                        ["pending_migrations"] = pending.ToList()
                    });
            }

            return HealthCheckResult.Healthy("Catalog database is healthy.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Catalog database is unreachable.",
                exception: ex);
        }
    }
}
```

Register the module health check in the module's registration class:

```csharp
public class CatalogModuleRegistration : IModuleRegistration
{
    public void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        // ... other registrations

        services.AddHealthChecks()
            .AddCheck<CatalogDatabaseHealthCheck>(
                "catalog-database",
                tags: ["ready", "catalog"]);
    }
}
```

### Step 5: Module-Specific Health Endpoints (Optional)

Expose a per-module health endpoint that only runs checks tagged with the module name:

```csharp
app.MapHealthChecks("/healthz/catalog", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("catalog"),
    ResponseWriter = WriteDetailedResponse
});
```

This lets operations teams check the health of individual modules independently.

### Step 6: External Dependency Checks

Create health checks for external services that your module depends on:

```csharp
public class PaymentGatewayHealthCheck : IHealthCheck
{
    private readonly HttpClient _httpClient;

    public PaymentGatewayHealthCheck(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("PaymentGateway");
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                "/health", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return HealthCheckResult.Healthy(
                    "Payment gateway is reachable.");
            }

            return HealthCheckResult.Degraded(
                $"Payment gateway returned {response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Payment gateway is unreachable.",
                exception: ex);
        }
    }
}
```

## Kubernetes Integration

For Kubernetes deployments, map the health endpoints to the standard probe paths:

```yaml
apiVersion: apps/v1
kind: Deployment
spec:
  template:
    spec:
      containers:
        - name: eshop
          livenessProbe:
            httpGet:
              path: /healthz
              port: 8080
            initialDelaySeconds: 5
            periodSeconds: 10
          readinessProbe:
            httpGet:
              path: /readyz
              port: 8080
            initialDelaySeconds: 10
            periodSeconds: 15
```

## Discussion

Health checks serve three distinct purposes:

1. **Liveness** (`/healthz`) -- Is the process running? If not, restart it. This check should have no dependencies -- it simply returns 200 OK if the application is alive.

2. **Readiness** (`/readyz`) -- Can the process serve traffic? This check verifies that all critical dependencies (database, message broker) are accessible. If the readiness check fails, the load balancer stops sending traffic until it recovers.

3. **Module health** (`/healthz/{module}`) -- Is a specific module operational? This provides granular visibility for operations teams to isolate issues.

Keep health checks lightweight. They run frequently (every 10-30 seconds) and should complete within a few hundred milliseconds. Avoid expensive operations like full table scans or large queries in health checks.

::: tip Health check UI
For a visual dashboard, add the `AspNetCore.HealthChecks.UI` package. It provides a web interface that displays the status of all registered health checks with history and alerting capabilities.
:::

## See Also

- [Aspire Integration](/aspire/) -- ServiceDefaults and preconfigured health endpoints
- [Messaging: Transports](/messaging/transports) -- Message broker connectivity
