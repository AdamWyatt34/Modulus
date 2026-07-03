## What

<!-- What does this PR change, in one or two sentences? -->

## Why

<!-- Link the issue or describe the motivation. -->

## Checklist

- [ ] `dotnet build Modulus.slnx` succeeds
- [ ] `dotnet test Modulus.slnx --filter "Category!=E2E&Category!=Integration"` passes
- [ ] New behavior is covered by tests
- [ ] Docs updated (`docs/`, package READMEs, or CHANGELOG.md) if user-facing
- [ ] No package versions added to individual `.csproj` files (central versions in `Directory.Packages.props`)
- [ ] Public API changes noted in CHANGELOG.md
