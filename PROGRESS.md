# remarkable-fullview — Progress

The build plan lives at `docs/plans/implementation.md`. Read Part A of that
document at the start of every session, then read this file to see where the
last session left off.

## Current stage

**Stage 0 — Foundations (repo, issues, CI skeleton)** — complete.
Next session starts **Stage 1 — Infrastructure (CDK) + deploy workflow**.

## Session log

### 2026-07-11 — Session 1 (Stage 0)

- Confirmed with Dan: build in the existing `remarkable-fullview` repo
  (`danjourno-dev/remarkable-fullview`), not a new `anchor` repo as the plan's
  literal text says. All "anchor" naming in the plan refers to this repo.
- Scaffolded `Fullview.sln` (classic `.sln` format — the .NET 10 SDK installed
  locally defaults `dotnet new sln` to `.slnx`, but CI pins the 8.0.x SDK via
  `actions/setup-dotnet@v4`, so the safer/older `.sln` format was created
  explicitly with `--format sln`).
- Created project skeletons under `/src`: `Fullview.Domain`, `Fullview.Rendering`,
  `Fullview.Api`, `Fullview.Infra` (all net8.0 classlibs), `Fullview.Device` (net8.0
  console app). `Fullview.Web` is a placeholder folder (React SPA arrives in
  Stage 6, not a .NET project).
- Created xUnit test projects under `/tests` mirroring the testable src
  projects (`Fullview.Domain.Tests`, `Fullview.Rendering.Tests`,
  `Fullview.Api.Tests`, `Fullview.Device.Tests`), referencing their corresponding
  src project. No test code yet — deliberately empty per the plan's Stage 0
  done criteria ("CI green on empty solution").
- Wired project references: `Fullview.Rendering` → `Fullview.Domain`;
  `Fullview.Api` → `Fullview.Domain`; `Fullview.Device` → `Fullview.Domain`,
  `Fullview.Rendering`.
- Verified locally: `dotnet build`, `dotnet test`, and
  `dotnet format --verify-no-changes` all pass (exit 0) on the empty
  solution.
- `.gitignore`: confirmed dotnet/node coverage already present; added
  `*.local.json`, `appsettings.Development.json`, `.env`, `.env.local`.
- Re-authenticated `gh` CLI (`gh auth login`, then `gh auth refresh -s
  project,read:project` for the board scope).
- Created 13 GitHub milestones (Stage 0 through Stage 9, Stage 6.5, Stage
  6.6, and v2 — Backlog) and area labels (`device`, `backend`, `web`,
  `infra`, `docs`, `task`; `good first issue` already existed).
- Seeded 67 GitHub issues from every stage's Build bullets in the plan,
  each labelled by area and assigned to its stage milestone. A handful are
  also tagged `good first issue`.
- Created GitHub Project "Anchor" (`danjourno-dev` user project #1, later renamed to "remarkable-fullview") and
  added all 67 issues to it.

## Decisions

- **Repo name:** using `remarkable-fullview` (already existed with origin
  set) instead of creating a new `anchor` repo.
- **Solution file format:** explicit classic `.sln`, not the SDK's default
  `.slnx`, to match the pinned 8.0.x SDK in CI.
- **Full rename (Session 2, Stage 1):** the product was originally going to
  be called ANCHOR; Dan renamed it to remarkable-fullview and asked for a
  full rename, not just the repo slug. `Anchor.*` projects/namespaces became
  `Fullview.*` (`Fullview.sln`, `Fullview.Domain`, `Fullview.Rendering`,
  `Fullview.Api`, `Fullview.Infra`, `Fullview.Device`, `Fullview.Web`, and
  matching `*.Tests` projects), and all prose mentions of "Anchor"/"ANCHOR"
  as the product name became "remarkable-fullview" across CLAUDE.md,
  README.md, CONTRIBUTING.md, ci.yml, bug.yml, .gitignore, and
  docs/plans/implementation.md (including the `repo:danjourno-dev/anchor:*`
  OIDC trust condition text and the `anchor-github-deploy` IAM role name in
  Checkpoint 1.1 — the actual OIDC/IAM resources get created with the new
  names when Stage 1 executes those checkpoints). The GitHub Project board
  (was "Anchor") was also renamed to "remarkable-fullview".

## Known issues / blockers

- None currently. The `Dan-613` keyring account shown by `gh auth status`
  is stale/inactive and harmless — the active account is `danjourno-dev`.

## Next up

- Checkpoint 0.1 confirmed: CI run `29156738756` succeeded on `main`.
  Stage 0 done criteria fully met.
- Start Stage 1 in the next session: `Fullview.Infra` CDK app (DynamoDB,
  HTTP API Gateway, placeholder Lambda, S3 inbox bucket, SSM params,
  budget/alarms), `cd-infra.yml`, and Checkpoint 1.1 (AWS <-> GitHub OIDC
  trust — Dan has not done this before, walk through click by click).
