# Contributing

## Building and testing locally

Requires the .NET 8 SDK.

```
dotnet build Anchor.sln
dotnet test Anchor.sln
dotnet format Anchor.sln --verify-no-changes
```

These are the same three commands CI runs (`.github/workflows/ci.yml`) on
every pull request. Run them before pushing.

## Conventions

- .NET 8, `Nullable` enabled, `ImplicitUsings` enabled.
- xUnit for tests, mirrored under `/tests` by src project.
- [Conventional commits](https://www.conventionalcommits.org/).
- All AWS infrastructure is defined as C# CDK in `Anchor.Infra` — no
  console-created resources.

## What not to commit

No secrets, ever: no AWS keys, no Anthropic API key, no device SSH
password, no account IDs. Local/machine-specific config goes in
`*.local.json`, `appsettings.Development.json`, or `.env` — all gitignored.
Ship an `.example` version of any config template instead.

## Where to start

Issues labelled `good first issue` are self-contained and don't require
deep familiarity with the rest of the codebase. The full build plan and
architecture rationale live in
[`docs/plans/implementation.md`](docs/plans/implementation.md).
