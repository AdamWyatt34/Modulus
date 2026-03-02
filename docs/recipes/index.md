# Recipes

Recipes are self-contained guides that show how to add specific capabilities to a Modulus solution. Each recipe follows a consistent Problem, Solution, Discussion format and can be applied independently.

## Available Recipes

| Recipe | Description |
|---|---|
| [Authentication](./authentication) | Secure module endpoints with JWT bearer authentication and authorization pipeline behaviors |
| [Caching](./caching) | Reduce database load with a distributed caching pipeline behavior for queries |
| [Strongly Typed IDs](./strongly-typed-ids) | Eliminate primitive obsession by wrapping entity IDs in type-safe wrappers |
| [Sagas](./sagas) | Coordinate multi-step workflows across modules using MassTransit state machines |
| [API Versioning](./api-versioning) | Evolve your API endpoints without breaking existing clients |
| [Health Checks](./health-checks) | Monitor module health in production with built-in and custom health check endpoints |

::: tip Recipes are additive
Each recipe builds on top of the standard Modulus scaffolding. You can apply any combination of recipes -- they do not conflict with each other or modify the core module structure.
:::
