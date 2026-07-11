# Anchor — Progress

The build plan lives at `docs/plans/implementation.md`. Read Part A of that
document at the start of every session, then read this file to see where the
last session left off.

## Current stage

**Stage 0 — Foundations (repo, issues, CI skeleton)** — in progress.

## Session log

### 2026-07-11 — Session 1 (Stage 0)

- Confirmed with Dan: build in the existing `remarkable-fullview` repo
  (`danjourno-dev/remarkable-fullview`), not a new `anchor` repo as the plan's
  literal text says. All "anchor" naming in the plan refers to this repo.
- Scaffolded `Anchor.sln` (classic `.sln` format — the .NET 10 SDK installed
  locally defaults `dotnet new sln` to `.slnx`, but CI pins the 8.0.x SDK via
  `actions/setup-dotnet@v4`, so the safer/older `.sln` format was created
  explicitly with `--format sln`).
- Created project skeletons under `/src`: `Anchor.Domain`, `Anchor.Rendering`,
  `Anchor.Api`, `Anchor.Infra` (all net8.0 classlibs), `Anchor.Device` (net8.0
  console app). `Anchor.Web` is a placeholder folder (React SPA arrives in
  Stage 6, not a .NET project).
- Created xUnit test projects under `/tests` mirroring the testable src
  projects (`Anchor.Domain.Tests`, `Anchor.Rendering.Tests`,
  `Anchor.Api.Tests`, `Anchor.Device.Tests`), referencing their corresponding
  src project. No test code yet — deliberately empty per the plan's Stage 0
  done criteria ("CI green on empty solution").
- Wired project references: `Anchor.Rendering` → `Anchor.Domain`;
  `Anchor.Api` → `Anchor.Domain`; `Anchor.Device` → `Anchor.Domain`,
  `Anchor.Rendering`.
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
- Created GitHub Project "Anchor" (`danjourno-dev` user project #1) and
  added all 67 issues to it.

## Decisions

- **Repo name:** using `remarkable-fullview` (already existed with origin
  set) instead of creating a new `anchor` repo. Internal naming (`Anchor.*`
  project/namespace prefix, "Anchor" as the product name) is unchanged from
  the plan — only the GitHub repo slug differs.
- **Solution file format:** explicit classic `.sln`, not the SDK's default
  `.slnx`, to match the pinned 8.0.x SDK in CI.

## Known issues / blockers

- None currently. The `Dan-613` keyring account shown by `gh auth status`
  is stale/inactive and harmless — the active account is `danjourno-dev`.

## Next up

- Review and commit the Stage 0 scaffold (not committed by the assistant
  per Dan's standing instruction — commits are always left for manual
  review).
- Push to origin/main and confirm the Actions tab shows a green CI run
  (Checkpoint 0.1) — this is the last open item for Stage 0's done
  criteria.
- Once confirmed green: Stage 0 is complete, move to Stage 1
  (infrastructure / CDK / deploy workflow).
