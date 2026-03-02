# Adding Authentication

## Problem

Your Modulus modules expose HTTP endpoints that need to be protected. You want to authenticate users with JWT bearer tokens and authorize access to specific endpoints based on claims or roles.

## Solution

Add JWT bearer authentication to the host application, apply authorization to endpoints using endpoint filters, and optionally create an `AuthorizationBehavior` pipeline behavior that enforces authorization rules at the mediator level.

### Step 1: Install Packages

Add the authentication package to the host project:

```bash
dotnet add src/EShop.Host/ package Microsoft.AspNetCore.Authentication.JwtBearer
```

### Step 2: Configure JWT Authentication

In the host's `Program.cs`, configure the authentication and authorization middleware:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configure JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Add middleware -- order matters
app.UseAuthentication();
app.UseAuthorization();

app.Run();
```

Add the JWT configuration to `appsettings.json`:

```json
{
  "Jwt": {
    "Issuer": "https://your-app.com",
    "Audience": "https://your-app.com",
    "Key": "your-secret-key-at-least-32-characters-long"
  }
}
```

::: warning Secret management
Never commit secret keys to source control. Use [.NET User Secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets), environment variables, or a vault service for production deployments.
:::

### Step 3: Secure Endpoints

Apply `RequireAuthorization()` to endpoint definitions in your module's Api layer:

```csharp
public class CreateProductEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/catalog", async (
            CreateProduct command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);

            return result.Match(
                onSuccess: id => Results.Created($"/catalog/{id}", id),
                onFailure: errors => Results.BadRequest(errors));
        })
        .RequireAuthorization()
        .WithName("CreateProduct")
        .WithTags("Catalog");
    }
}
```

For role-based authorization:

```csharp
app.MapDelete("/catalog/{id:guid}", async (
    Guid id,
    IMediator mediator,
    CancellationToken ct) =>
{
    var result = await mediator.Send(new DeleteProductCommand(id), ct);

    return result.Match(
        onSuccess: () => Results.NoContent(),
        onFailure: errors => Results.NotFound(errors));
})
.RequireAuthorization(policy => policy.RequireRole("Admin"));
```

### Step 4: Authorization Pipeline Behavior (Optional)

For more granular authorization that lives at the command/query level rather than the endpoint level, create a pipeline behavior:

```csharp
// Marker interface for commands/queries that require authorization
public interface IAuthorized
{
    string RequiredPermission { get; }
}
```

```csharp
// Command that requires a specific permission
public sealed record DeleteProductCommand(Guid ProductId) : ICommand, IAuthorized
{
    public string RequiredPermission => "catalog:delete";
}
```

```csharp
using System.Security.Claims;

public sealed class AuthorizationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IAuthorized
    where TResponse : Result
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthorizationBehavior(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var user = _httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            return (TResponse)(object)Result.Failure(
                Error.Unauthorized(
                    "Auth.NotAuthenticated",
                    "Authentication is required."));
        }

        var permissions = user.FindAll("permission")
            .Select(c => c.Value)
            .ToHashSet();

        if (!permissions.Contains(request.RequiredPermission))
        {
            return (TResponse)(object)Result.Failure(
                Error.Forbidden(
                    "Auth.InsufficientPermission",
                    $"Permission '{request.RequiredPermission}' is required."));
        }

        return await next();
    }
}
```

Register the behavior and `IHttpContextAccessor`:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddPipelineBehavior(typeof(AuthorizationBehavior<,>));
```

::: tip Register after validation
Place the `AuthorizationBehavior` after the `ValidationBehavior` in the pipeline so invalid requests are rejected before the authorization check runs. This avoids unnecessary authorization lookups for bad input.

```csharp
services.AddPipelineBehavior(typeof(UnhandledExceptionBehavior<,>));
services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
services.AddPipelineBehavior(typeof(AuthorizationBehavior<,>));
services.AddPipelineBehavior(typeof(MetricsBehavior<,>));
```
:::

## Discussion

There are two complementary approaches to authorization in a Modulus solution:

1. **Endpoint-level authorization** via `RequireAuthorization()` -- simple, declarative, and sufficient for most use cases. Use this when authorization is based on authentication status or broad roles.

2. **Pipeline-level authorization** via `AuthorizationBehavior` -- more flexible, testable, and composable. Use this when authorization depends on fine-grained permissions or when the same command can be dispatched from multiple entry points (endpoints, background jobs, event handlers).

You can combine both approaches. Use endpoint authorization as the first line of defense (rejecting unauthenticated requests before they reach the mediator) and the pipeline behavior for fine-grained permission checks.

## See Also

- [Pipeline Behaviors](/mediator/pipeline-behaviors) -- How pipeline behaviors work
- [Result Pattern](/mediator/result-pattern) -- `Error.Unauthorized` and `Error.Forbidden`
