# CLAUDE.md

Project ANCHOR — a reMarkable 1 executive-function assistant. Full product
design and staged build plan: `docs/plans/implementation.md`. Session state
and decisions log: `PROGRESS.md`.

**Read both of those before starting work in this repo.** The plan's Part A
governs how sessions are run (one stage at a time, blocking checkpoints,
restate done criteria before coding).

## Public repo rules

This is a public open-source repo.

- No secrets ever land in committed files: no AWS keys, no Anthropic API
  key, no device SSH password, no account IDs. Secrets live in GitHub
  Actions secrets or AWS SSM Parameter Store. Config ships as `.example`
  templates.
- Write docs (README, architecture docs, setup instructions) for a stranger
  forking the repo, not just for Dan. Anything Dan-specific belongs in
  gitignored local config (`*.local.json`, `appsettings.Development.json`,
  `.env`), never in code.

## Solution layout

```
/src
  Anchor.Domain          shared entities + sync metadata (netstandard-safe)
  Anchor.Rendering        ImageSharp screen renderer (shared device/server)
  Anchor.Api              Lambda handlers (sync, capture, auth)
  Anchor.Infra             CDK app (DynamoDB, API GW, Lambdas, S3, budgets/alarms)
  Anchor.Device            rM1 app (fb0 blitter, evdev input, SQLite, sync engine)
  Anchor.Web               React SPA (capture + management + needs-review)
/tests                     mirrors src, one *.Tests project per testable src project
/docs                      the plan, runbooks, device setup notes
```

Single solution: `Anchor.sln` (classic `.sln` format, not `.slnx` — CI pins
the 8.0.x SDK via `actions/setup-dotnet@v4`).

## Conventions

- .NET 8, `Nullable` enabled, `ImplicitUsings` enabled.
- xUnit for tests.
- Conventional commits.
- `dotnet format` enforced in CI (`--verify-no-changes`).
- All infra as C# CDK — no console-created AWS resources except where a plan
  checkpoint explicitly calls for a manual step.

## Build / test commands

```
dotnet build Anchor.sln
dotnet test Anchor.sln
dotnet format Anchor.sln --verify-no-changes
```

These three commands are exactly what CI runs (`.github/workflows/ci.yml`).
Run them locally before pushing.
