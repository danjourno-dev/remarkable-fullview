# remarkable-fullview — Progress

The build plan lives at `docs/plans/implementation.md`. Read Part A of that
document at the start of every session, then read this file to see where the
last session left off.

## Current stage

**Stage 2 — Domain + Sync API — done.** Deployed (`fullview-sync` Lambda,
`gsi1` index, `POST /sync` route all live), both post-deploy bugs (JSON
casing, duplicate `entityType` discriminator — Sessions 5-6) fixed and
redeployed. `tools/seed-data` and the `Category=Integration` convergence
test both pass against the live stack, closing Stage 2's Done criteria.
Stage 3 is next — see below.

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

### 2026-07-11 — Session 4 (Stage 2, code)

- `Fullview.Domain`: `SyncContext` enum; abstract `SyncEntity` base (Id, Context,
  UpdatedAt, UpdatedBy, Deleted, computed `SortKey`) using
  `[JsonPolymorphic]`/`[JsonDerivedType]` (native to `System.Text.Json` on
  .NET 8, no extra package) so a `List<SyncEntity>` round-trips through JSON
  without a custom converter. Eight concrete entities per B5/B3: `Todo`,
  `AgendaEvent` (with Source/ExternalId/ExternalEtag/ReadOnly for the Stage
  6.5 Google puller), `Meal`, `ShoppingItem`, `Recipe`, `Routine`,
  `RoutineCheck`, `InboxPage`. `SyncRequest`/`SyncResponse` protocol DTOs.
- `Fullview.Api`: `ISyncStore` abstraction + `SyncService` (pure LWW/idempotent
  apply + delta-pull logic, no AWS types) + `DynamoSyncStore` (real
  DynamoDB-backed implementation using the Document API; entities stored
  whole as a `data` JSON blob alongside queryable `pk`/`sk`/`entityType`/
  `context`/`updatedAt`/`deleted`/`gsi1pk`/`gsi1sk` attributes). New
  `SyncFunction` Lambda (same package as `HealthFunction`, different
  handler) parses the POST body, calls `SyncService`, returns 200/400.
- `Fullview.Infra`: added `gsi1` (gsi1pk/gsi1sk) to the existing table for
  the UpdatedAt-ordered delta query; added `SyncFunction` + `POST /sync`
  route + `table.GrantReadWriteData` + its own CloudWatch error alarm,
  mirroring the existing `HealthFunction` wiring.
- Tests: `Fullview.Domain.Tests` covers polymorphic JSON round-trips.
  `Fullview.Api.Tests` has an `InMemorySyncStore` test double plus
  `SyncServiceTests` covering the plan's named conflict cases — newer
  offline edit beats an older stored edit, a stale edit arriving late is
  discarded, replaying the same mutation is idempotent, delete produces a
  tombstone that shows up in the delta, and two `SyncService` instances
  sharing a store converge. All run in the default `dotnet test`, no AWS
  needed.
- `tools/seed-data`: small console app (added to `Fullview.sln` under a new
  `tools` solution folder) that POSTs a handful of sample Todo/Meal/
  ShoppingItem entities to a running `/sync` endpoint via
  `FULLVIEW_API_BASE_URL`. Not a test project, just a manual convenience —
  mirrors the `tools/google-auth` pattern planned for Stage 6.5.
- Verified locally: `dotnet build`, `dotnet test --filter "Category!=Integration"`
  (8/8 pass), `dotnet format --verify-no-changes`, and `cdk synth` (with
  dummy `FULLVIEW_ALERT_EMAIL`/`CDK_DEFAULT_ACCOUNT`) all pass.
- **Not done yet:** nothing has been deployed. Pushing this to `main` will
  trigger `cd-infra.yml`'s `deploy` job, which needs Dan's approval on the
  `production` environment gate (same mechanism as Stage 1) before the new
  GSI/Lambda/route actually exist. The literal Done criteria ("integration
  test drives two fake clients to convergence against deployed stack") is
  written but only exercises the real thing once `FULLVIEW_API_BASE_URL` is
  set and it's run manually — see Decisions.

### 2026-07-11 — Session 5 (Stage 2, post-deploy fix)

- Stage 2 deployed successfully (`fullview-sync` Lambda + `gsi1` index +
  `POST /sync` route all live; `GET /health` still returns 200).
- **Found and fixed a real bug while smoke-testing the live endpoint:**
  `SyncFunction` deserialized the request body with
  `JsonSerializer.Deserialize<SyncRequest>(request.Body)` (no options) —
  that's case-sensitive PascalCase-only. A camelCase body (`{"deviceId":...}`,
  what `tools/seed-data`, any JS web client, and `System.Net.Http.Json`'s own
  *defaults* all send) failed `DeviceId`'s `required` check and was rejected
  as 400 "Request body is not valid JSON" — a misleading message, since the
  JSON was syntactically fine, just case-mismatched. Confirmed via curl:
  `{"DeviceId":...}` (exact PascalCase) succeeded, `{"deviceId":...}` didn't.
  Fixed by adding `Fullview.Api.Sync.SyncJson.Options`
  (`JsonSerializerDefaults.Web` — camelCase + case-insensitive) and using it
  in both `SyncFunction` (request/response) and `DynamoSyncStore` (the
  internal `data` blob, for one consistent convention). No existing rows in
  the table to migrate — nothing had been successfully written yet. Rebuilt,
  reran the full unit suite (still 8/8) and `dotnet format` — clean. **Not
  yet redeployed** — this fix needs another push-to-main + approval cycle
  before the seed script / live integration test will work.

### 2026-07-11 — Session 6 (Stage 2, post-deploy fix #2)

- Session 5's fix deployed and confirmed live: a raw `curl` with a camelCase,
  empty-outbox body now returns 200 with a camelCase response.
- Re-ran `tools/seed-data` against the live stack — still failed with 400
  "Request body is not valid JSON." Lambda logs (`aws logs tail
  /aws/lambda/fullview-sync`, needs `MSYS_NO_PATHCONV=1` in git-bash or the
  leading `/` gets path-mangled) showed a different exception on a later
  request: `System.Text.Json.JsonException: Deserialized object contains a
  duplicate type discriminator metadata property. Path: $.entityType` —
  thrown from `DynamoSyncStore.Deserialize`, i.e. on *read back*, not on the
  initial write.
- **Root cause:** every concrete entity (`Todo`, `AgendaEvent`, etc.)
  overrides `SyncEntity.EntityType`, and the base declaration is
  `[JsonIgnore]`, but `[JsonIgnore]` on an abstract property does not carry
  over to the derived override for System.Text.Json's reflection-based
  serializer — each entity was emitting **both** the polymorphic
  discriminator (`"entityType"`, from `[JsonPolymorphic]`) **and** its own
  reflected `"EntityType"` property. Confirmed by printing the actual
  outgoing JSON from `tools/seed-data`: every object had
  `{"entityType":"Todo","EntityType":"Todo",...}`. `System.Text.Json`
  refuses to deserialize an object with a duplicate discriminator, so the
  entity that got stuck in the table from earlier ad-hoc `curl` debugging
  (`Todo#abc123`, written before this was understood) poisoned every
  subsequent delta query — a 500, not a 400, since it happened on read.
- **Fix:** added `[JsonIgnore]` directly to the `EntityType` override in all
  8 entity classes (`Todo`, `AgendaEvent`, `Meal`, `ShoppingItem`, `Recipe`,
  `Routine`, `RoutineCheck`, `InboxPage`).
- Cleared the single poisoned row from the live table by hand (`aws dynamodb
  delete-item` on `fullview-app`, `pk=USER#dan`/`sk=Todo#abc123` — confirmed
  via Dan the table only had dev/debug data, safe to wipe; table itself is
  CDK-managed and was left in place, only the item was deleted).
  Post-delete scan confirmed 0 items.
- Rebuilt, reran the full unit suite (still passes) and `dotnet format` —
  clean.
- Deployed. Re-ran `tools/seed-data` against the live stack — seeded 4
  entities successfully. Re-ran `FULLVIEW_API_BASE_URL=... dotnet test
  Fullview.sln --filter Category=Integration` against the live stack — the
  two-fake-clients convergence test passed. **Stage 2's Done criteria are
  now met.**

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
- **Stage 1 — budget is USD, not GBP.** First real deploy attempt failed:
  `AWS::Budgets::Budget` rejected `Unit: GBP` because this AWS account's
  billing currency is USD — `BudgetLimit.Unit` must match it. Changed to
  `12 USD` (rough £10 equivalent at time of writing) to satisfy the plan's
  "£10/mo guardrail" intent without fighting the account's fixed currency.
- **Stage 2 — dropped `SyncCursor` as a stored entity.** B5 lists it among
  the table's entities, but with single-user v1 there's no per-device state
  worth persisting server-side: the cursor is just the max `UpdatedAt` seen
  so far, and each client already holds its own last cursor locally. Modelled
  it as an opaque string the client passes back, not a DynamoDB row. Revisit
  if v2 (Cognito, multi-device-aware server state) needs it.
- **Stage 2 — the convergence integration test is HTTP-based, not a raw
  DynamoDB SDK test.** It POSTs to the real deployed `/sync` endpoint (two
  simulated clients, one offline-edit-then-web-edit scenario) rather than
  hitting DynamoDB directly, so it only needs the public API base URL, not
  AWS credentials. It's tagged `[Trait("Category","Integration")]` and
  excluded from `ci.yml`'s default `dotnet test` run (that job has no AWS/
  deploy context) — run it manually with `FULLVIEW_API_BASE_URL` set once
  Stage 2 is deployed. This mirrors how `cd-infra.yml` already separates
  AWS-credentialed jobs from the plain `ci.yml` build/test job.
- **Stage 2 — no auth added to `/sync` yet.** B5's "Auth: single-user v1 —
  API key in device/web config from SSM" is an architecture-wide note, but
  Stage 2's own Build bullet doesn't call out adding it, so `/sync` stays
  open like `/health` for now, same trust model as today. Flagging this to
  Dan: worth deciding which stage actually wires up the API key check
  (Stage 6 web app and Stage 7 capture pipeline both eventually need real
  auth) rather than letting it slide by default.

## Known issues / blockers

- None currently. The `Dan-613` keyring account shown by `gh auth status`
  is stale/inactive and harmless — the active account is `danjourno-dev`.

## Next up

- Checkpoint 0.1 confirmed: CI run `29156738756` succeeded on `main`.
  Stage 0 done criteria fully met.
- **Checkpoint 1.1 — done.** OIDC provider already existed in the account
  (from a prior project). Dan created the `remarkable-fullview-github-deploy`
  role with the correct trust policy via the console; Claude Code (with AWS
  CLI + `gh` CLI, at Dan's request) attached the least-privilege inline
  policy (`sts:AssumeRole` on `cdk-hnb659fds-*-255550683596-eu-west-2`),
  set the `AWS_DEPLOY_ROLE_ARN`/`FULLVIEW_ALERT_EMAIL` repo variables via
  `gh variable set`, and created the `production` environment with
  `danjourno-dev` as required reviewer via `gh api`.
- **Checkpoint 1.2 — done, no action needed.** Account was already
  bootstrapped (`CDKToolkit` stack `CREATE_COMPLETE`, default qualifier
  `hnb659fds`, region eu-west-2) from the same prior project. Verified the
  bootstrap role trust chain (`cdk-hnb659fds-deploy-role-*` trusts the
  account root) lines up with the deploy role's assume-role policy.
- First deploy attempt failed: `AWS::Budgets::Budget` rejected
  `Unit: GBP` (account bills in USD). Fixed to `12 USD` — see Decisions.
- **Checkpoint 1.3 — done.** Re-deploy succeeded: `fullview-stack` is
  `CREATE_COMPLETE`. Verified `GET https://vqnmcbnti3.execute-api.eu-west-2.amazonaws.com/health`
  returns `{"status":"ok"}` (HTTP 200).
- **Stage 1 done criteria fully met.**
- **Stage 2 fully done.** Both post-deploy bugs (Session 5 JSON casing,
  Session 6 duplicate discriminator) fixed and deployed. Live seed script
  and `Category=Integration` convergence test both pass against the
  deployed stack. Next session: start Stage 3 (device hello-world) — note
  Checkpoint 3.1 there is already done (see Decisions/Current stage above),
  so work starts from Checkpoint 3.2.
