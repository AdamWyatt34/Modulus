# Prerequisites & Installation

Modulus is a .NET global tool that scaffolds production-ready modular monolith solutions. This page covers everything you need to install and start using the CLI.

## Prerequisites

### .NET 10 SDK

Modulus targets .NET 10.0. You need the SDK installed to both run the CLI and build generated solutions.

[Download .NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

Verify your installation:

```bash
dotnet --version
# 10.0.100 or later
```

### Docker (optional)

The generated solution includes integration tests powered by [Testcontainers](https://dotnet.testcontainers.org/). If you plan to run integration tests locally, you need Docker Desktop or a compatible container runtime.

::: tip Not required for getting started
Docker is only needed for integration tests. You can scaffold solutions, build, and run unit tests without it.
:::

## Installation

Install the Modulus CLI as a .NET global tool:

```bash
dotnet tool install --global Modulus.Cli
```

## Verify Installation

After installation, confirm the CLI is available:

```bash
modulus version
```

This outputs the installed version of Modulus. If you receive a "command not found" error, ensure that the .NET global tools directory is on your `PATH`.

::: info .NET global tools path
The default global tools directory is:
- **Windows**: `%USERPROFILE%\.dotnet\tools`
- **macOS / Linux**: `$HOME/.dotnet/tools`

Add the appropriate path to your shell profile if it is not already present.
:::

## Update

To update to the latest version:

```bash
dotnet tool update --global Modulus.Cli
```

## Uninstall

To remove Modulus:

```bash
dotnet tool uninstall --global Modulus.Cli
```

## NuGet Packages

Modulus ships as a CLI tool plus a set of companion library packages. The CLI automatically references the correct packages when scaffolding a solution, so you do not need to install them manually.

| Package | Description |
| --- | --- |
| [`Modulus.Cli`](https://www.nuget.org/packages/Modulus.Cli) | CLI tool for scaffolding modular monolith solutions |
| [`Modulus.Mediator`](https://www.nuget.org/packages/Modulus.Mediator) | CQRS mediator with pipeline behaviors and Result pattern |
| [`Modulus.Mediator.Abstractions`](https://www.nuget.org/packages/Modulus.Mediator.Abstractions) | Mediator interfaces, Result types, and pipeline contracts |
| [`Modulus.Messaging`](https://www.nuget.org/packages/Modulus.Messaging) | MassTransit messaging with multi-transport and outbox support |
| [`Modulus.Messaging.Abstractions`](https://www.nuget.org/packages/Modulus.Messaging.Abstractions) | Messaging interfaces and integration event contracts |

::: tip Abstractions packages
The `Abstractions` packages contain only interfaces and contracts with zero third-party dependencies. Reference them in your Domain and Application layers to keep those layers clean. The implementation packages (`Modulus.Mediator` and `Modulus.Messaging`) are referenced only in Infrastructure and host projects.
:::

## What's Next

Now that Modulus is installed, walk through the end-to-end tutorial to create your first modular monolith solution.

[Your First Solution](/getting-started/first-solution)
