# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-02-28

### Added

- **CLI Tool (`Modulus.Cli`)**
  - `modulus init` command to scaffold a new modular monolith solution
  - `modulus add-module` command to add feature modules with full layer structure
  - `modulus list-modules` command to list all modules in a solution
  - `modulus version` command to display the CLI version
  - `--aspire` flag for .NET Aspire AppHost and ServiceDefaults integration
  - `--transport` flag to configure messaging transport (InMemory, RabbitMQ, Azure Service Bus)
  - `--no-git` flag to skip git initialization
  - `--no-endpoints` flag to create modules without an API layer

- **Mediator (`Modulus.Mediator` + `Modulus.Mediator.Abstractions`)**
  - CQRS mediator with `ICommand`, `IQuery`, `IStreamQuery`, and `IDomainEvent` support
  - `Result` and `Result<T>` types with typed `Error` values
  - `ValidationResult` for FluentValidation integration
  - Configurable pipeline behaviors (`IPipelineBehavior<TRequest, TResponse>`)
  - Built-in `ValidationBehavior` for automatic FluentValidation execution
  - Built-in `LoggingBehavior` for request timing and outcome logging
  - Built-in `UnhandledExceptionBehavior` for exception-to-Result conversion
  - Assembly scanning via Scrutor for automatic handler registration

- **Messaging (`Modulus.Messaging` + `Modulus.Messaging.Abstractions`)**
  - `IMessageBus` abstraction for publishing integration events and sending commands
  - `IntegrationEvent` base record with `EventId`, `OccurredOn`, and `CorrelationId`
  - MassTransit integration with pluggable transports (InMemory, RabbitMQ, Azure Service Bus)
  - Transactional outbox pattern with `IOutboxStore` and `OutboxProcessor`
  - Entity Framework Core outbox implementation (`EfOutboxStore`)
  - Automatic handler discovery and consumer adapter registration

[1.0.0]: https://github.com/adamwyatt34/Modulus/releases/tag/v1.0.0
