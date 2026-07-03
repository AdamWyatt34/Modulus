# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in any ModulusKit package, please report it privately so we can investigate and ship a fix before public disclosure.

**Preferred:** open a [GitHub private vulnerability report](https://github.com/adamwyatt34/Modulus/security/advisories/new).

**Alternate:** email `adamrwyatt1@gmail.com` with subject `ModulusKit security`.

Please include:

- The affected package and version.
- A minimal reproduction (steps, code, or PoC).
- The impact you've observed or suspect.
- Any suggested mitigation.

We will acknowledge receipt within five business days and provide a remediation timeline once we've reproduced the issue. We aim to ship fixes for confirmed High/Critical issues within 30 days.

## Supported Versions

Only the latest published `ModulusKit.*` release line receives security updates. The project is pre-1.0; once 1.0 ships, the latest minor of the current major will be supported.

## Dependency Hygiene

- All dependencies are pinned via `Directory.Packages.props`.
- CI runs `dotnet list package --vulnerable --include-transitive` on every push and weekly on a schedule; High or Critical advisories fail the build.
- Messaging depends only on actively maintained OSS broker clients (`RabbitMQ.Client`, `Azure.Messaging.ServiceBus`); the previously pinned, end-of-life MassTransit 7.x dependency has been removed in favor of an in-house transport layer.
