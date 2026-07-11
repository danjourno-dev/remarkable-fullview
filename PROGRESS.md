# remarkable-fullview — Progress

The build plan lives at `docs/plans/implementation.md`. Read Part A of that
document at the start of every session, then read this file to see where the
last session left off.

## Current stage

**Stage 1 — Infrastructure (CDK) + deploy workflow** — code complete,
blocked on Dan for Checkpoints 1.1-1.3 (all require manual AWS/GitHub
console or CLI actions). See Decisions and Next up below.

**Stage 3, Checkpoint 3.1 (device prep) is already complete** — done by Dan
outside any tracked session, ahead of Stage 3 itself. See Decisions below;
do not redo it when Stage 3 comes up.

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

### 2026-07-11 — Session 3 (Stage 1, code)

- Built `Fullview.Infra` as a single-stack CDK v2 (C#) app (`FullviewStack`):
  DynamoDB table (`fullview-app`, PK `pk`/SK `sk` per B5, PAY_PER_REQUEST,
  PITR, `RemovalPolicy.RETAIN`), S3 inbox bucket (private, SSE-S3, RETAIN),
  HTTP API + `/health` route backed by a placeholder Lambda
  (`Fullview.Api/Functions/HealthFunction.cs`), a CloudWatch alarm on that
  Lambda's errors, and an AWS Budget (£10/mo, 80% actual + 100% forecasted
  email alerts) — all wired to one SNS topic emailing an address supplied at
  deploy time.
- Added `.github/workflows/cd-infra.yml`: PR -> `cdk diff` posted as a PR
  comment; push to main -> `cdk deploy` gated behind the `production`
  GitHub environment (Dan as required reviewer). Both jobs assume
  `AWS_DEPLOY_ROLE_ARN` (repo variable) via OIDC — no stored AWS keys.
- Verified locally: `dotnet build`/`test`/`format --verify-no-changes` all
  pass, and `cdk synth` produces a valid template (required bumping the
  global `aws-cdk` CLI from 2.1124.1 to 2.1130.0 — the pinned
  `Amazon.CDK.Lib` 2.261.0 needs CLI >= 2.1126.0).
- Did not yet run any Checkpoint 1.1-1.3 manual step (OIDC trust, `cdk
  bootstrap`, first deploy) — those are blocking and need Dan.

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
- **Stage 3 / Checkpoint 3.1 done early, out of band:** Dan completed device
  prep ahead of the plan's normal stage order. Device shipped on OS
  **3.27.3.0**, outside Toltec's supported ceiling (3.3.2.1666), so this
  project uses **Vellum** (`vellum-dev/vellum-cli`) instead of Toltec — no
  firmware downgrade. Steps completed: SSH password recorded, automatic
  updates turned off, vellum-cli bootstrapped over USB, `vellum check-os`
  confirmed all packages compatible. Launcher installed: **AppLoad**
  (`vellum add appload`, currently `appload-0.5.3-r0`). remux is Toltec-only
  and absent from Vellum's index, so it is not used anywhere in this
  project — every launcher reference in the plan means AppLoad. This
  updates `docs/plans/implementation.md` Checkpoint 3.1 and the B4 nav note
  accordingly.
- **Stage 1 — HTTP API built from L1 (`Cfn*`) constructs, not the L2 `HttpApi` +
  Lambda integration helper.** The .NET binding for
  `Amazon.CDK.AWS.Apigatewayv2.Integrations` (the package with
  `HttpLambdaIntegration`) never shipped past a 2020-era `2.0.0-alpha.0` on
  NuGet, so it's not usable. `FullviewStack` wires the API by hand instead:
  `CfnApi` + `CfnIntegration` (`AWS_PROXY`, payload format 2.0) + `CfnRoute`
  + `CfnStage` (`$default`, `AutoDeploy=true`) + `Function.AddPermission`
  for the invoke grant. Same CloudFormation output as the L2 would have
  produced; revisit if AWS ever ships a stable .NET package.
- **Stage 1 — no AWS account id, alert email, or other Dan-specific value
  committed anywhere.** `EnvironmentSettings.FromEnvironment()` reads
  `CDK_DEFAULT_ACCOUNT`/`CDK_DEFAULT_REGION` (set by the `cdk` CLI from
  whichever credentials are active — Dan's local profile, or the
  OIDC-assumed role in CI) and requires `FULLVIEW_ALERT_EMAIL` as an env
  var, sourced from a `FULLVIEW_ALERT_EMAIL` GitHub repo variable in CI.
  Region defaults to `eu-west-2` if unset. This keeps the public repo's CDK
  code identical for any forker per Stage 9's "deploying your own instance"
  requirement.
- **Stage 1 — no SSM parameters created by CDK.** The plan's "SSM params"
  bullet is satisfied by later checkpoints (6.5.1, 7.1) that create real
  secret values manually via `aws ssm put-parameter --type SecureString`.
  CDK never declares a parameter with a placeholder value, because any
  value baked into the template gets pushed back on the next `cdk deploy`,
  silently clobbering whatever Dan set by hand. IAM grants for reading
  `/fullview/*` parameters will be added to the relevant Lambda's role in
  the stage that first needs them (Stage 2 for the API key, Stage 6.5/7 for
  Google/Anthropic).
- **Stage 1 — Lambda deploy package built in CI, not by CDK asset
  bundling.** `cd-infra.yml` runs `dotnet lambda package` (Amazon.Lambda.Tools)
  before `cdk diff`/`cdk deploy` and passes the zip path via
  `LAMBDA_PACKAGE_PATH`. `FullviewStack.ResolveLambdaAsset()` falls back to
  the raw `Fullview.Api` source directory when that env var is unset so
  `cdk synth`/`cdk diff` still work locally — that fallback is a valid
  synth-time asset (a real zip of source files) but NOT a deployable Lambda
  package, so a real `cdk deploy` always needs `LAMBDA_PACKAGE_PATH` set
  first.

## Known issues / blockers

- None currently. The `Dan-613` keyring account shown by `gh auth status`
  is stale/inactive and harmless — the active account is `danjourno-dev`.

## Next up

- Checkpoint 0.1 confirmed: CI run `29156738756` succeeded on `main`.
  Stage 0 done criteria fully met.
- Stage 1 code is in place (`Fullview.Infra`, `Fullview.Api/Functions/HealthFunction.cs`,
  `.github/workflows/cd-infra.yml`) but nothing has been pushed/merged or
  deployed yet. Blocking on Dan for, in order:
  - **Checkpoint 1.1** — AWS IAM OIDC provider + `remarkable-fullview-github-deploy`
    role (trust policy scoped to `repo:danjourno-dev/remarkable-fullview:*`),
    plus GitHub repo variables `AWS_DEPLOY_ROLE_ARN` and `FULLVIEW_ALERT_EMAIL`,
    plus the `production` environment with Dan as required reviewer.
  - **Checkpoint 1.2** — `cdk bootstrap` in Dan's AWS account, region eu-west-2.
  - **Checkpoint 1.3** — first workflow deploy; Dan approves the environment
    gate, confirms the stack in the console and `GET /health` returns
    `{"status":"ok"}`.
- Once 1.1-1.3 are confirmed, update this file's "Current stage" to Stage 1
  complete and hand off to Stage 2 (Domain + Sync API).
