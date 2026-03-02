# Contributing Guide

Thank you for your interest in contributing to Modulus. This guide covers everything you need to set up a local development environment, run tests, and submit a pull request.

## Prerequisites

### Required

- **.NET 10 SDK** -- [Download](https://dotnet.microsoft.com/download/dotnet/10.0). Verify with `dotnet --version`.
- **Git** -- [Download](https://git-scm.com/downloads).

### Optional

- **Node.js 20+** -- Only needed for building and previewing the documentation site. [Download](https://nodejs.org/).
- **Docker** -- Only needed for running integration tests locally.

## Clone and Build

```bash
git clone https://github.com/adamwyatt34/Modulus.git
cd Modulus
dotnet build
```

The solution should build with zero warnings. If you see warnings, fix them before submitting a PR.

## Project Structure

```
Modulus/
├── src/
│   ├── Modulus.Cli/                       # CLI tool (global tool entry point)
│   ├── Modulus.Templates/                 # Solution and module templates
│   ├── Modulus.Mediator/                  # Mediator implementation
│   ├── Modulus.Mediator.Abstractions/     # Mediator interfaces and contracts
│   ├── Modulus.Messaging/                 # Messaging implementation (MassTransit)
│   └── Modulus.Messaging.Abstractions/    # Messaging interfaces and contracts
├── tests/
│   ├── Modulus.Cli.Tests/                 # CLI integration tests
│   ├── Modulus.Mediator.Tests/            # Mediator unit tests
│   └── Modulus.Messaging.Tests/           # Messaging unit tests
├── docs/                                  # VitePress documentation site
└── .github/                               # GitHub Actions workflows
```

| Project | Description |
|---|---|
| `Modulus.Cli` | The `modulus` global tool. Handles `init`, `add-module`, `add-entity`, `add-command`, `add-query`, `add-endpoint`, `list-modules`, and `version` commands. |
| `Modulus.Templates` | Scriban templates used by the CLI to generate solution and module files. |
| `Modulus.Mediator` | The CQRS mediator implementation: dispatcher, pipeline behaviors, and handler resolution. |
| `Modulus.Mediator.Abstractions` | Public interfaces: `IMediator`, `ICommand`, `IQuery`, `Result`, `Error`, and pipeline contracts. Zero external dependencies. |
| `Modulus.Messaging` | MassTransit integration: transport configuration, outbox/inbox, and integration event dispatch. |
| `Modulus.Messaging.Abstractions` | Public interfaces: `IMessageBus`, `IIntegrationEvent`, and consumer contracts. Zero external dependencies. |

## Running Tests

Run the full test suite:

```bash
dotnet test
```

Run tests for a specific project:

```bash
dotnet test tests/Modulus.Mediator.Tests/
dotnet test tests/Modulus.Cli.Tests/
dotnet test tests/Modulus.Messaging.Tests/
```

::: tip Watch mode
During development, use `dotnet watch test` to re-run tests automatically when files change:

```bash
dotnet watch test --project tests/Modulus.Mediator.Tests/
```
:::

## Running the CLI Locally

You can run the CLI from source without installing it as a global tool:

```bash
dotnet run --project src/Modulus.Cli -- init TestSolution
```

The `--` separates `dotnet run` arguments from arguments passed to the CLI. For example:

```bash
# Initialize a new solution with Aspire support
dotnet run --project src/Modulus.Cli -- init TestSolution --aspire

# Add a module
dotnet run --project src/Modulus.Cli -- add-module Catalog

# Add an entity
dotnet run --project src/Modulus.Cli -- add-entity Product --module Catalog
```

::: info Working directory matters
The CLI commands that modify a solution (`add-module`, `add-entity`, etc.) expect to be run from the generated solution's root directory. After running `init`, `cd` into the generated directory before running additional commands.
:::

## Documentation Development

The documentation site is built with [VitePress](https://vitepress.dev/). To develop locally:

```bash
cd docs
npm install
npm run docs:dev
```

This starts a development server at `http://localhost:5173` with hot module replacement. Changes to markdown files are reflected immediately.

### Building the Docs

```bash
npm run docs:build
```

The built site is output to `docs/.vitepress/dist/`.

### Documentation Structure

```
docs/
├── .vitepress/
│   └── config.mts          # VitePress configuration (nav, sidebar, theme)
├── getting-started/         # Installation and first steps
├── architecture/            # Module anatomy, building blocks, extraction
├── cli/                     # CLI command reference
├── mediator/                # CQRS mediator documentation
├── messaging/               # Integration events and transports
├── aspire/                  # Aspire integration
├── testing/                 # Unit, integration, and architecture tests
├── recipes/                 # How-to guides for common patterns
├── contributing/            # This guide
└── index.md                 # Landing page
```

## Pull Request Guidelines

### Before You Start

1. **Check existing issues.** Look for an open issue related to your change. If none exists, consider opening one first to discuss the approach.
2. **Branch from `main`.** Create a feature branch from the latest `main`:

```bash
git checkout main
git pull origin main
git checkout -b feature/your-feature-name
```

### Making Changes

1. **Write tests.** Every code change should have corresponding tests. New features need tests. Bug fixes need a test that reproduces the bug.
2. **Run the full test suite.** Before pushing, make sure all tests pass:

```bash
dotnet test
```

3. **Follow existing patterns.** Look at how similar features are implemented in the codebase and follow the same conventions.
4. **Keep commits focused.** Each commit should represent a single logical change. Use descriptive commit messages.

### Commit Messages

Write clear, descriptive commit messages:

```
Add streaming query support to the mediator

- Implement IStreamQuery<T> and IStreamQueryHandler<T, TResult>
- Add Stream<T> method to IMediator
- Register streaming handlers via Scrutor assembly scanning
- Add unit tests for streaming dispatch
```

- Use the imperative mood ("Add", not "Added" or "Adds")
- First line: 50-72 characters, summarizing the change
- Body (optional): explain the "why" behind the change, not just the "what"

### Submitting the PR

1. Push your branch:

```bash
git push -u origin feature/your-feature-name
```

2. Open a pull request against `main` on GitHub.
3. Fill in the PR description:
   - **What** does this PR do?
   - **Why** is this change needed?
   - **How** can reviewers test it?
4. Ensure CI passes. The GitHub Actions workflow runs `dotnet build` and `dotnet test` on every PR.

### PR Review Checklist

Reviewers will check:

- [ ] Tests are included and passing
- [ ] Code follows existing patterns and conventions
- [ ] No unnecessary dependencies added
- [ ] Public API changes are backward-compatible (or documented as breaking)
- [ ] Documentation is updated if user-facing behavior changed

## Code Style

The repository includes an `.editorconfig` that enforces coding style. Your IDE should pick it up automatically.

Key conventions:

- **File-scoped namespaces** -- Use `namespace Foo.Bar;` (not block-scoped)
- **Sealed by default** -- Mark classes as `sealed` unless inheritance is explicitly needed
- **Records for DTOs and messages** -- Use `sealed record` for commands, queries, events, and DTOs
- **Explicit access modifiers** -- Always specify `public`, `private`, `internal`, etc.
- **No `var` for non-obvious types** -- Use explicit types when the type is not obvious from the right-hand side

::: tip EditorConfig enforcement
The CI build treats warnings as errors. If your IDE's formatting differs from `.editorconfig`, run `dotnet format` before committing to auto-fix style issues:

```bash
dotnet format
```
:::

## Release Process

Modulus uses GitHub Actions for CI/CD:

1. **On every push and PR** -- The CI workflow runs `dotnet build` and `dotnet test`.
2. **On version tags** -- When a tag matching `v*` (e.g., `v1.2.0`) is pushed, the CD workflow:
   - Builds the solution in Release configuration
   - Packs all NuGet packages (`Modulus.Cli`, `Modulus.Mediator`, `Modulus.Mediator.Abstractions`, `Modulus.Messaging`, `Modulus.Messaging.Abstractions`)
   - Publishes to [NuGet.org](https://www.nuget.org/)
   - Creates a GitHub Release with release notes

Version numbers follow [Semantic Versioning](https://semver.org/):

- **MAJOR** -- Breaking changes to public APIs
- **MINOR** -- New features, backward-compatible
- **PATCH** -- Bug fixes, backward-compatible

## Getting Help

- **GitHub Issues** -- [Report bugs or request features](https://github.com/adamwyatt34/Modulus/issues)
- **Discussions** -- [Ask questions or share ideas](https://github.com/adamwyatt34/Modulus/discussions)

## See Also

- [Prerequisites & Installation](/getting-started/) -- Setting up Modulus as a user
- [CLI Reference](/cli/) -- The commands that the CLI provides
