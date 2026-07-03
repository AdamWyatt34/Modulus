using Microsoft.EntityFrameworkCore;
using Modulus.Messaging;
using Modulus.Messaging.DependencyInjection;
using SampleApp.BuildingBlocks.Application.DependencyInjection;
using SampleApp.Notifications.Infrastructure;
using SampleApp.Orders.Infrastructure.Persistence;
using SampleApp.Orders.Integration.IntegrationEvents;
using SampleApp.WebApi; // generated AddModulusHandlers / AddAllModules / MapAllModuleEndpoints
using SampleApp.WebApi.Middleware;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();

// SECURITY: AddAuthentication() below registers no scheme — the scaffold runs unauthenticated.
// Replace with your real configuration before deploying (JWT bearer, OIDC, Keycloak, etc.):
//
//   builder.Services.AddAuthentication("Bearer")
//       .AddJwtBearer("Bearer", o => { /* Authority, Audience, etc. */ });
//
// Once you have a scheme registered, enable per-group enforcement by switching the
// MapGroup(...) call in each module's <ModuleName>EndpointRegistration.cs to use
// .RequireAuthorization().
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddApplicationServices();
builder.Services.AddModulusHandlers();
builder.Services.AddAllModules(builder.Configuration);

// Per-module configuration: uncomment to load modules.{name}.json files
// builder.Configuration.AddModuleConfiguration("Catalog", "Ordering");

// ModulusKit.Messaging: binds the "Messaging" section from appsettings.json
// (Transport=InMemory here — RabbitMQ / Azure Service Bus transports ship as separate
// packages and need one extra AddModulus*Transport() call). The callback supplies the
// assemblies that host integration event types and IIntegrationEventHandler<> consumers.
builder.Services.AddModulusMessaging(builder.Configuration, options =>
{
    // OrderPlaced event type (Orders.Integration) — needed to resolve incoming messages.
    options.Assemblies.Add(typeof(OrderPlaced).Assembly);
    // OrderPlacedHandler consumer (Notifications.Infrastructure).
    options.Assemblies.Add(typeof(NotificationsModule).Assembly);
});

// Transactional outbox + inbox on SQLite. The EF Core migrations for both contexts were
// generated into Migrations/Outbox and Migrations/Inbox with `dotnet ef` (see README).
builder.Services.AddModulusOutbox(o => o.UseSqlite(
    builder.Configuration.GetConnectionString("Default"),
    b => b.MigrationsAssembly(typeof(Program).Assembly.GetName().Name)));
builder.Services.AddModulusInbox(o => o.UseSqlite(
    builder.Configuration.GetConnectionString("Default"),
    b => b.MigrationsAssembly(typeof(Program).Assembly.GetName().Name)));

var app = builder.Build();

// Apply pending outbox + inbox migrations at startup (no-op if the contexts are not registered).
await app.UseModulusMessagingMigrationsAsync();

// Dev-time convenience for the module databases: real apps manage per-module EF migrations.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.EnsureCreated();
    scope.ServiceProvider.GetRequiredService<SampleApp.Notifications.Infrastructure.Persistence.NotificationsDbContext>().Database.EnsureCreated();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthChecks("/healthz");
app.MapHealthChecks("/readyz");

app.MapAllModuleEndpoints();

app.Run();

public partial class Program;
