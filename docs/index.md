---
layout: home

hero:
  name: "Modulus"
  text: "Modular Monolith Toolkit for .NET"
  tagline: Scaffold production-ready modular monoliths in seconds
  actions:
    - theme: brand
      text: Get Started
      link: /getting-started/
    - theme: alt
      text: View on GitHub
      link: https://github.com/adamwyatt34/Modulus
  image:
    src: /logo.svg
    alt: Modulus

features:
  - icon: ">_"
    title: CLI Scaffolding
    details: Scaffold solutions, modules, entities, commands, queries, and endpoints with a single command. Aspire-ready out of the box.
    link: /cli/
    linkText: CLI Reference
  - icon: "{ }"
    title: CQRS Mediator
    details: Lightweight mediator with commands, queries, streaming, domain events, pipeline behaviors, and the Result pattern. No MediatR dependency.
    link: /mediator/
    linkText: Mediator Docs
  - icon: "<< >>"
    title: Messaging & Outbox
    details: Integration events over MassTransit with InMemory, RabbitMQ, and Azure Service Bus transports. Transactional outbox included.
    link: /messaging/
    linkText: Messaging Docs
  - icon: "\U0001F3D7"
    title: Clean Architecture
    details: Each module gets Domain, Application, Infrastructure, Api, and Integration layers with enforced boundaries via architecture tests.
    link: /architecture/
    linkText: Architecture Guide
  - icon: "\U0001F50C"
    title: Microservice Extraction Path
    details: Modules are isolated by design. When you're ready to scale, extract any module to a standalone service with zero business logic changes.
    link: /architecture/extraction
    linkText: Extraction Guide
  - icon: "\u2728"
    title: Aspire Integration
    details: Optional .NET Aspire support for service discovery, distributed telemetry, health checks, and the developer dashboard.
    link: /aspire/
    linkText: Aspire Docs
---

## Quick Install

```bash
dotnet tool install --global Modulus.Cli
```

## Quick Start

```bash
# Create a new modular monolith
modulus init EShop --aspire --transport rabbitmq

# Add feature modules
cd EShop
modulus add-module Catalog
modulus add-module Orders

# Scaffold CQRS components
modulus add-entity Product --module Catalog --aggregate --properties "Name:string,Price:decimal"
modulus add-command CreateProduct --module Catalog --result-type Guid
modulus add-query GetProductById --module Catalog --result-type ProductDto
modulus add-endpoint CreateProduct --module Catalog --method POST --route / --command CreateProduct --result-type Guid

# Run it
dotnet run --project src/EShop.WebApi
```
