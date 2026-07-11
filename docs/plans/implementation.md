# Project ANCHOR — reMarkable 1 Executive Function Assistant
## Claude Code Build Plan (staged handover document)

**Owner:** Dan · **Stack:** C#/.NET 8 everywhere · AWS API Gateway + Lambda + DynamoDB (CDK in C#) · **GitHub (public repo) for source, Issues/Projects for tracking, GitHub Actions for CI/CD** · Anthropic API for handwriting capture · reMarkable 1 (armhf Linux) as primary device.

**This is a PUBLIC open-source repo.** Two consequences Claude Code must respect at all times:
- **No secrets in the repo, ever.** No AWS keys, no Anthropic API key, no device SSH password, no account IDs in committed files. All secrets live in GitHub Actions secrets or AWS SSM. Config files ship as `.example` templates. `.gitignore` covers `*.local.json`, `appsettings.Development.json`, `.env`.
- **Write for strangers.** README, LICENSE (MIT), architecture docs, and setup instructions that work for someone who isn't Dan. Anything Dan-specific (his AWS account, his device, his data) goes in gitignored local config, not in code.

---

# PART A — INSTRUCTIONS TO CLAUDE CODE (read first, every session)

1. **This plan is executed one stage per session.** At the start of every session: read `PROGRESS.md` at repo root, confirm the current stage with Dan, and restate the stage's Done Criteria before writing any code.
2. **Human checkpoints are blocking.** Wherever this plan says `⏸ CHECKPOINT`, stop, give Dan exact step-by-step instructions for the manual action (portal clicks, commands, physical device steps), and **do not proceed until he explicitly confirms completion**. Never assume a manual step succeeded.
3. **Token discipline.** Commit and push in small increments. Before context runs long, write a session summary into `PROGRESS.md` (what's done, what's next, any deviations from this plan) so the next session can resume cold. Prefer finishing a stage cleanly over starting the next.
4. **Deviations:** if reality contradicts this plan (library doesn't work on armhf, firmware mismatch, etc.), propose the smallest change, record it in `PROGRESS.md` under "Decisions", and continue. Do not silently redesign.
5. **Dan's working style:** concrete sequential steps, one thing at a time, explicit scope boundaries. When guiding manual steps, number them and keep each step a single action.
6. **Conventions:** .NET 8, nullable enabled, xUnit, `dotnet format` in CI. Single solution `Anchor.sln`. Conventional commits. All infra as C# CDK — no console-created resources except where a checkpoint says so.

**Repo layout (created in Stage 0):**
```
/src
  Anchor.Domain          shared entities + sync metadata (netstandard-safe)
  Anchor.Rendering       ImageSharp screen renderer (shared device/server)
  Anchor.Api             Lambda handlers (sync, capture, auth)
  Anchor.Infra           CDK app (DynamoDB, API GW, Lambdas, S3, budgets/alarms)
  Anchor.Device          rM1 app (fb0 blitter, evdev input, SQLite, sync engine)
  Anchor.Web             minimal React SPA (capture + management + needs-review)
/tests                   mirrors src
/docs                    this plan, PROGRESS.md, runbooks, device setup notes
/.github
  /workflows             ci.yml, cd-infra.yml, cd-web.yml, release-device.yml
  /ISSUE_TEMPLATE        bug.yml, task.yml
README.md                public-facing: what it is, screenshots, setup
LICENSE                  MIT
CONTRIBUTING.md          how to build/test locally
```

---

# PART B — PRODUCT DESIGN

## B1. Purpose
An always-visible, distraction-free external brain for executive function: what matters *now*, what's *next*, and frictionless capture of anything in the head — with AI turning handwritten scribbles into structured items. It must feel 100% functional with no WiFi.

## B2. Design principles
1. **Glanceable over interactive.** The rM1 is primarily read; capture-heavy input happens on phone/web or via pen. Every screen answers its question in <3 seconds.
2. **Now/Next beats calendars.** Time-blindness support: the top strip of every screen shows current block, next block, and time until it. This strip is sacred — always present, always accurate to local data.
3. **Capture must be cheaper than remembering.** Pen Inbox: write anything, walk away. AI files it. No taxonomy decisions at capture time.
4. **Trust through reliability.** Offline-first; the board never blanks, inputs never lost. Stale is clearly labelled ("synced 07:40 · 3 pending"), never hidden.
5. **Low decoration.** 16-grey e-ink, big type, generous whitespace, max ~7 items visible per list (overflow = "+4 more"). No icons that need learning.
6. **Kind defaults.** Unfinished todos roll to today automatically (no guilt backlog on the board); "Needs review" catches uncertain AI captures instead of polluting lists.

## B3. Feature set (v1 unless marked)
- **Work / Personal modes (core v1 concept):** every entity belongs to a context (`Work` | `Personal`). The device is always in exactly one mode; all screens filter to it. **Toggle = one tap** on the persistent mode badge in the Now/Next strip (top-right of every screen) — no menu, no navigation. Mode is device-local state (not synced), so the board on your desk can sit in Work while your phone browses Personal. Screen sets differ per mode: *Personal* = Today, Todos, Meals, Shopping, Routines; *Work* = Today, Todos, Agenda, Work Routine (startup/shutdown ritual). **Strip exception (important):** the Now/Next strip is *always cross-context* — it merges agenda from both contexts and shows the true current and next commitments regardless of mode, each tagged with a context marker (W/P). Mode filters everything *below* the strip; the strip itself is the single source of "what's actually next in my life" and never hides anything.
- **Today dashboard:** Now/Next strip · today's agenda column · top 3 focus todos · meals today · shopping count · Inbox status ("2 pages awaiting WiFi").
- **Todos:** flat list with priority (Focus/Normal/Someday), optional due date, energy tag (quick-win / deep). Tap to complete (partial-refresh strikethrough). Auto-rollover.
- **Agenda — both contexts come from Google Calendar (CONFIRMED WORKING, not speculative).** Anchor pulls **two** Google calendars via one puller:
  - **Primary Google calendar → `Context=Personal`** (home/personal events, entered by Dan directly in Google).
  - **`Work (mirror)` Google calendar → `Context=Work`** (populated by a Power Automate flow that mirrors his work Outlook calendar every 30 min — see Stage 6.6; the flow is built and verified).

  This is the single most important simplification in the project: **there is exactly one calendar integration, not two.** No Microsoft Graph, no ICS, no second auth flow, no second code path. The puller reads a list of `(calendarId → Context)` pairs; adding the work calendar is a config line, not a feature. Anything typed or handwritten directly into Anchor is an app-native event (`Source=Native`) and coexists with pulled events in the same agenda card.

  Pulled events are **read-only on the device**: they render and feed the Now/Next strip but can't be edited or deleted there (a pulled event is a mirror, not a master). Sync is **one-way, Google → Anchor**; Anchor never writes back to Google. Microsoft Graph and ICS publishing are both explicitly ruled out — do not propose either.
- **Meal plan:** week grid, breakfast/dinner slots, links to recipes; "tap meal → recipe screen".
- **Shopping list:** grouped by aisle-ish category (AI assigns on capture), tap to tick, auto-clears ticked section daily.
- **Recipes:** simple title/ingredients/steps; "add ingredients to shopping" action from web app.
- **Routines (v1.5):** morning/evening checklist screens that reset daily — checklist-as-ritual is high-value for ADHD.
- **Inbox capture:** designated xochitl notebook; strokes → server → Claude vision → classified JSON → entities; confidence <threshold lands in Needs Review (web app).
- **Web app:** fast add/edit for everything, needs-review triage, recipe editor, device status.
- **Sync everywhere:** single `/sync` endpoint; outbox/LWW as specified in B5.

## B4. Screen layouts (renderer targets, 1404×1872 portrait)
```
┌──────────────────────────────────────┐
│ NOW  (W) Deep work: Totalia [ WORK ]◄┼─ mode badge: 1 tap toggles mode
│ NEXT (P) Pick up Talia    in 1h 40m  │  ← strip is ALWAYS cross-context
├──────────────────────────────────────┤     (W/P tags); mode filters below
│ TODAY            Thu 9 Jul           │
│ ────────────                         │
│ 09:00 Standup                        │
│ 11:30 —                              │
│ 13:00 Lunch w/ Talia                 │
│                                      │
│ FOCUS 3                              │
│ [ ] NOTICE file attribution          │
│ [ ] Book Yael gym session            │
│ [ ] Reply to recruiter               │
│                                      │
│ MEALS   B: overnight oats            │
│         D: chilli (batch)            │
│ SHOPPING 6 items   INBOX 1 page ⏳    │
│         synced 07:40 · all clear     │
└──────────────────────────────────────┘
```
Navigation: left/right edge tap = prev/next screen (Today ↔ Todos ↔ Meals ↔ Shopping ↔ Routine). Screen order fixed; current screen name in footer. remux gesture switches to xochitl Inbox for writing.

## B5. Architecture (binding decisions)
- **Single-table DynamoDB** (`PK=USER#dan`, `SK=<TYPE>#<ULID>`), entities: Todo, AgendaEvent, Meal, ShoppingItem, Recipe, Routine, RoutineCheck, InboxPage, SyncCursor. All rows carry `UpdatedAt`, `UpdatedBy`, `Deleted` (tombstone), and `Context` (`Work` | `Personal`; Meals/Shopping/Recipes are implicitly Personal). Current mode is **device-local UI state** (SQLite settings table, not synced); the `/sync` protocol is context-agnostic — filtering happens at render time.
- **AgendaEvent additionally carries:** `Source` (`Native` | `GoogleCalendar`), `ExternalId` (Google's event id — the idempotency key for pulled events), `ExternalEtag`, `ReadOnly` (true for pulled events). **Pulled events are exempt from last-write-wins**: Google is the master for anything with `Source=GoogleCalendar`, so the puller overwrites local copies unconditionally, and deletions in Google produce tombstones here. Never merge a pulled event with a local edit — the device UI prevents the edit in the first place.
- **Sync:** `POST /sync` — request `{deviceId, cursor, outbox[]}`; mutations ULID-keyed and idempotent; conflicts = per-entity last-write-wins; response = delta since cursor + new cursor. Same endpoint for device and web.
- **Device app:** .NET 8 self-contained linux-arm; ImageSharp render → `/dev/fb0` + MXCFB ioctl; evdev touch; SQLite; outbox in same transaction as every local write; systemd service; RTC wake ~30 min + wake-on-touch.
- **Capture:** device watcher ships changed Inbox `.rm` pages via outbox → S3; Lambda renders strokes to PNG, calls Claude (vision) with strict JSON-only classification prompt (todo|shopping|meal|recipe-note|agenda|unknown + confidence); files entities; page marked processed.
- **Auth:** single-user v1 — API key in device/web config from SSM Parameter Store. Cognito is v2.
- **Cost guardrails:** CDK deploys an AWS Budget alarm (£10/mo) and CloudWatch alarms on Lambda errors.

---

# PART C — STAGES

> Each stage = one Claude Code session. Format: **Goal → Build → ⏸ Checkpoints → Done Criteria.**

## Stage 0 — Foundations (repo, issues, CI skeleton)
**Note for Claude Code: Dan has used Azure DevOps pipelines for years but has NEVER set up GitHub Actions. Do not assume familiarity. When you write the first workflow file, explain the mapping explicitly — ADO `trigger:` → GH `on:`; ADO `pool:` → GH `runs-on:`; ADO `steps:` → GH `steps:` (largely the same); ADO tasks (`DotNetCoreCLI@2`) → GH `run:` shell commands or marketplace `uses:` actions; ADO variable groups → GH repo secrets/variables; ADO service connections → GH OIDC federated identity (see Stage 1). Keep workflows plain and readable rather than clever.**

**Build:** solution + project skeletons per layout; `PROGRESS.md`; `CLAUDE.md` (conventions, build/test commands, this plan's location); MIT `LICENSE`; first-pass `README.md`; `.gitignore` (dotnet + node + `*.local.json`); `.github/workflows/ci.yml` — on pull_request and push to main: `actions/checkout`, `actions/setup-dotnet@v4` (8.0.x), restore, `dotnet format --verify-no-changes`, build, `dotnet test`. Create GitHub Issues for the current stage's tasks and a GitHub Project (board) with Stages 0–9 as milestones.
**⏸ CHECKPOINT 0.1:** Dan creates the **public** GitHub repo (`anchor` under `danjourno-dev`), runs `git remote add origin` + first push. Give him the exact commands. Confirm the Actions tab shows the CI run.
**Done:** CI green on empty solution; issues + milestones visible; repo public with README and LICENSE.

## Stage 1 — Infrastructure (CDK) + deploy workflow
**Build:** `Anchor.Infra` CDK app — DynamoDB table, API Gateway (HTTP API), placeholder Lambda, S3 bucket (inbox pages), SSM params, budget + alarms. `.github/workflows/cd-infra.yml`: on PR → `cdk diff` posted as a PR comment; on push to main → `cdk deploy`. Uses `permissions: id-token: write` + `aws-actions/configure-aws-credentials@v4` with `role-to-assume` (OIDC — **no long-lived AWS keys stored in GitHub**). Environment `production` with required reviewer = Dan, so deploys need his click.

**⏸ CHECKPOINT 1.1 — AWS ↔ GitHub trust (this replaces the ADO service connection; Dan has not done this before, so walk him through it click by click):**
1. In AWS IAM → Identity providers → Add provider → OpenID Connect. Provider URL `https://token.actions.githubusercontent.com`, audience `sts.amazonaws.com`.
2. Create an IAM role (`anchor-github-deploy`) with a trust policy restricting `token.actions.githubusercontent.com:sub` to `repo:danjourno-dev/anchor:*` — **Claude Code must generate this exact JSON for him** and explain why the `sub` condition matters (without it, any repo on GitHub could assume the role).
3. Attach a least-privilege deploy policy (Claude Code generates; scope to the CDK-created resources + CloudFormation).
4. Add the role ARN as a GitHub **repository variable** `AWS_DEPLOY_ROLE_ARN` (a variable, not a secret — an ARN isn't sensitive, and this keeps the workflow readable).
5. Explain the model in one line: GitHub mints a short-lived OIDC token per run, AWS trades it for temporary credentials. Nothing persistent to leak — this is why we're not using access keys.

**⏸ CHECKPOINT 1.2:** `cdk bootstrap` in Dan's AWS account (exact commands; confirm region — default eu-west-2).
**⏸ CHECKPOINT 1.3:** first workflow deploy — Dan approves the environment gate, confirms stack in console and `GET /health` returns 200.
**Done:** deployed stack, workflow-driven via OIDC, health check green, zero AWS secrets in GitHub.

## Stage 2 — Domain + Sync API
**Build:** `Anchor.Domain` entities + sync metadata; `Anchor.Api` `/sync` handler (idempotent apply, LWW, delta query via GSI on UpdatedAt); seed script; xUnit suite incl. conflict cases (offline edit vs newer web edit; replayed mutation; tombstone).
**Done:** integration test drives two fake clients to convergence against deployed stack.

## Stage 3 — Device hello-world (the derisking stage)
**Build:** `Anchor.Device` skeleton; fb0 P/Invoke blitter + MXCFB refresh (reference libremarkable struct layout); ImageSharp "Hello Dan" render; `publish-arm.sh` (self-contained linux-arm); deploy-over-SSH script.
**⏸ CHECKPOINT 3.1:** device prep — **DEVIATION FROM ORIGINAL PLAN, already decided: the device shipped on OS 3.27.3.0, which is far outside Toltec's supported ceiling (3.3.2.1666). Rather than downgrade firmware, this project uses VELLUM (`vellum-dev/vellum-cli`), the actively-maintained successor that tracks current OS versions. No downgrade, no Toltec.** Dan follows the numbered guide: record SSH password (Settings → Help → Copyright and licenses), turn OFF automatic updates, SSH in over USB (`ssh root@10.11.99.1` — Windows has OpenSSH built in), bootstrap vellum-cli, `vellum check-os`, install a launcher. Record the exact OS version and vellum package list in PROGRESS.md.
**Launcher caveat:** remux may be Toltec-only and not yet in Vellum's index — Claude Code must check `vellum search` and pick whatever launcher Vellum actually ships (AppLoad / Oxide / remux), then update this plan's references accordingly.
**⏸ CHECKPOINT 3.2:** run hello-world binary on device; Dan confirms text on screen + memory footprint (`free`, RSS of process) recorded.
**Fallback rule:** if .NET-on-fb0 is blocked after honest effort, record it, and pivot device shell to rmkit/C++ thin client with server-rendered PNGs (Tier 1/2 degrade) — renderer stays C# server-side. Do not burn more than one extra session before invoking this.
**Done:** pixels on e-ink from our binary, deploy script repeatable.

## Stage 4 — Local-first device app
**Build:** SQLite store + migrations (incl. device-local settings: current mode); `Anchor.Rendering` screens (Today, Todos, Agenda, Meals, Shopping, Recipe) with region-map hit-testing — the agenda card appears in **both** modes, filtered by context (Personal agenda is Google-backed from Stage 6.5; Work agenda is native in v1); **mode badge in Now/Next strip — single tap toggles Work/Personal, swaps screen set, re-renders with partial refresh; strip itself always renders cross-context with W/P tags per B3**; evdev touch → tap-to-complete with partial refresh; edge-tap navigation; seed data (seed both contexts); systemd unit; footer sync-status (static for now).
**⏸ CHECKPOINT 4.1:** Dan lives with seeded board for a day; feedback captured as GitHub Issues (layout/type-size tweaks are expected here).
**Done:** fully offline interactive dashboard on device.

## Stage 5 — Device sync engine
**Build:** outbox drain + delta apply against `/sync`; connectivity probe; retry/backoff; RTC-wake timer unit; "synced HH:MM · n pending" footer live.
**⏸ CHECKPOINT 5.1:** kill WiFi test — Dan ticks items offline, restores WiFi, confirms convergence with web/API data.
**Done:** two-way sync proven incl. offline queueing.

## Stage 6 — Web app
**Build:** `Anchor.Web` (React, minimal, mobile-friendly): Work/Personal context switcher mirroring the device (plus an "All" view for triage); quick-add bar (natural date parsing, defaults new items to current context), lists CRUD, meal-week editor, recipe editor + "add ingredients to shopping", needs-review triage (empty for now), device status. `.github/workflows/cd-web.yml` deploys to S3+CloudFront via the same OIDC role.
**Done:** Dan manages all data from phone browser; syncs to device.

## Stage 6.5 — Google Calendar sync (BOTH agendas: Personal + Work)
**Goal:** one puller, two calendars. The board's Personal agenda mirrors Dan's primary Google calendar; the Work agenda mirrors the `Work (mirror)` calendar that Power Automate fills from Outlook (Stage 6.6 — **already built and verified**). Everything keeps working offline (last-pulled events persist in SQLite).

**Design rule (do not deviate): the puller is context-agnostic and calendar-driven.** Config is a list of pairs:
```
calendars:
  - id: <primary google calendar id>       context: Personal
  - id: <work-mirror google calendar id>   context: Work
```
Adding, removing, or re-tagging a calendar must be a **config change, not a code change**. There is no work-specific code path anywhere in the backend — the work agenda is just another Google calendar that happens to be populated by a flow.

**Build:**
- `Anchor.Api` — `CalendarPullFunction` Lambda on an EventBridge schedule (every 15 min). For **each** configured calendar, call Google Calendar API `events.list` with `singleEvents=true` (expands recurring events into instances — do NOT render RRULEs on-device), `orderBy=startTime`, window **now−1d to now+8d**.
- **Use `syncToken` incremental sync per calendar**, not a full re-fetch: store `nextSyncToken` **keyed by calendar id** in DynamoDB. On `410 Gone`, discard that calendar's token and do one full re-fetch. (Note: the Work mirror churns more than the Personal calendar because the flow rebuilds it every 30 min, so expect more deltas on that one — this is expected, not a bug.)
- Map Google events → `AgendaEvent` with `Source=GoogleCalendar`, `ReadOnly=true`, `ExternalId=<google event id>`, and **`Context` taken from the calendar's config mapping, never inferred from event content**. Upsert by `ExternalId` (idempotent). `status=cancelled` → tombstone.
- **All-day events**: Google returns `date` not `dateTime` — handle both; render all-day events as a band at the top of the agenda card, not a timed row.
- **Timezones**: store UTC, render Europe/London. Test across a BST boundary — this is where these integrations habitually break.
- Web app (Stage 6) gains **Settings → Calendars**: connect/disconnect Google, list calendars with their `Context` mapping, show last-pull time and errors per calendar.
- Device: pulled events render with a subtle marker and **no tap-to-edit hit region**; they feed the Now/Next strip identically to native events (and the strip, per B3, shows both contexts regardless of mode — this is precisely why both calendars must be pulled even when the board is in one mode).

**⏸ CHECKPOINT 6.5.1 — Google Cloud OAuth setup (Dan has not done this before; walk him through it click by click):**
1. Google Cloud Console → create project `anchor-personal`.
2. Enable the **Google Calendar API**.
3. OAuth consent screen → **External**, publishing status stays **Testing** with Dan as the sole test user (correct choice — avoids Google's app-verification process entirely; a Testing-mode refresh token is fine for single-user use).
4. Credentials → OAuth client ID → **Desktop app** (avoids hosting a redirect URI).
5. Client ID + secret go straight into **AWS SSM Parameter Store as SecureString** — never the repo, never GitHub secrets.

**⏸ CHECKPOINT 6.5.2 — one-time consent + refresh token:** Claude Code provides a small local console app (`tools/google-auth/`) Dan runs once on Windows. It opens the browser, he consents, it prints a **refresh token** → into SSM. The Lambda uses it to mint short-lived access tokens each run; Dan never re-consents. Scope must be **`calendar.readonly`** — read-only, so even a compromised token cannot alter Dan's calendars. **One consent covers both calendars** (they're on the same Google account) — this is the whole reason the two-calendar model costs nothing extra.

**⏸ CHECKPOINT 6.5.3 — live verification (both contexts):** Dan adds a test event to his primary Google calendar → confirm it appears on the board in **Personal** mode. Then confirms a real work meeting (mirrored by the Stage 6.6 flow) appears in **Work** mode with `Context=Work`. Finally, confirm **both** show in the Now/Next strip regardless of which mode the board is in.

**Open-source note (README):** a forker brings their **own Google Cloud project, own OAuth client, own refresh token in their own SSM**, and configures their own calendar→context mapping. The Outlook-mirroring flow (Stage 6.6) is documented as an optional recipe, not a dependency — the puller neither knows nor cares how a calendar got populated.

**Done:** both agendas on the device reflect Google within ~15 min, survive offline, and Anchor has never written a byte back to Google.

## Stage 6.6 — Work calendar bridge (Outlook → Google mirror) — **ALREADY BUILT AND VERIFIED**
**Status: DONE, outside the repo.** Dan has built and tested this flow. Claude Code does **not** need to build it — only to (a) document it in the repo as a recipe, and (b) make sure Stage 6.5's puller reads the mirror calendar it produces. Do not re-litigate this design.

**Why this shape:** the employer's tenant blocks ICS calendar publishing, and Microsoft Graph would require an Entra app registration + admin consent — **explicitly out of scope, no IT ticket, do not propose it.** Power Automate is already sanctioned in the tenant, and both the Office 365 Outlook and Google Calendar connectors are **standard tier**, so the flow runs at **zero additional licence cost** under existing M365 seeded rights. (HTTP and custom-connector actions are premium — marked with a diamond icon — and Dan has standard connectors only. **The flow therefore must never call Anchor's API directly.**)

**The flow as built** (scheduled cloud flow, every 30 min):
1. `Google Calendar — List the events on a calendar` (`Work (mirror)`, window `utcNow()` → `addDays(utcNow(), 3)`)
2. `Apply to each` → `Google Calendar — Delete an event`
3. `Office 365 Outlook — Get calendar view of events (V3)` (same 3-day window)
4. `Apply to each` (over `body/value`) → `Google Calendar — Create an event`

**Wipe-and-rebuild, deliberately.** Rather than diffing, the flow clears the mirror and rebuilds it each run. This is idempotent by construction: cancellations disappear (never recreated), moved meetings land at the right time, and nothing can duplicate. It also means **no marker strings, no matching expressions, and no state** — chosen after a more "clever" diffing design proved unreasonably fiddly to build in Power Automate's expression editor.

**Two gotchas Dan hit — record these in the repo docs so a stranger doesn't hit them:**
- In the second `Apply to each`, the array to select is **`body/value`** (the event collection). Power Automate also offers singular fields like `Categories`; those are properties of one event and will not work.
- Google's `Create an event` rejects Outlook's plain `start`/`end` fields (`String/date-no-tz`) with `OpenApiOperationParameterValidationFailed`. **Use the "Start time with time zone" / "End time with time zone" fields** from the V3 action instead. (Expression fallback if they're absent: `formatDateTime(items('Apply_to_each_1')?['start'], 'yyyy-MM-ddTHH:mm:ssZ')` — but only correct if the Outlook action returns UTC.)

**Consequence for Stage 6.5:** the mirror is fully rebuilt every 30 min, so Google event IDs churn. The puller must therefore tolerate a steady stream of delete+create deltas on that calendar. **If ghosting/refresh churn on the e-ink board becomes a problem, the fix belongs in the puller** (dedupe by `(start, subject)` before writing to DynamoDB so a delete+recreate of an unchanged meeting is a no-op) — **not** by making the flow cleverer. Flag this to Dan if observed; don't pre-optimise.

**Privacy decision (Dan's call, record whichever he picked in PROGRESS.md):** the flow can mirror real meeting subjects, or mirror time-blocks only with a fixed title (`Work: Busy`). The board's core job is time-blindness support, which a titled block serves either way. Sensitive meetings marked Private in Outlook are already redacted by Outlook itself.

**Backend cost of all this: one config line.** The `Work (mirror)` calendar id is added to the Stage 6.5 calendar list with `context: Work`. There is no other work-calendar code in Anchor.

## Stage 7 — Handwriting capture pipeline## Stage 7 — Handwriting capture pipeline
**Build:** device watcher on Inbox notebook (`~/.local/share/remarkable/xochitl/`) → page upload via outbox→S3; Lambda: `.rm` stroke parse → PNG (port the documented v5 stroke format to C#); Claude vision call (strict JSON schema, per-item confidence, **and per-item context Work|Personal** — deterministic overrides first: shopping/meal/recipe → Personal always; ambiguous context = Needs Review even when the type is confident); entity filing + Needs Review below threshold; InboxPage state machine (queued→processed→filed) surfaced on Today screen. **Captured agenda items are created as `Source=Native` (never pushed to Google — Anchor stays strictly read-only against Google Calendar); they sit alongside pulled events in the same agenda card.**
**⏸ CHECKPOINT 7.1:** Anthropic API key into **AWS SSM Parameter Store** (guide). It must NEVER be a GitHub secret or appear in the repo — the Lambda reads it from SSM at runtime.
**⏸ CHECKPOINT 7.2:** live test — Dan writes a mixed page (todo + shopping + event), confirms correct filing; tune prompt with real samples.
**Done:** scribble-to-structured round trip works, uncertain items quarantined.

## Stage 8 — Executive-function polish
**Build:** Now/Next strip logic everywhere (cross-context merge of both agendas, W/P tags); auto-rollover job (Lambda, scheduled, per-context); routines screens with daily reset — Personal morning/evening **and Work startup/shutdown rituals** (the shutdown checklist doubles as tomorrow's Focus-3 picker: last item is "choose tomorrow's 3"); shopping auto-clear; ghosting hygiene (full refresh cadence); power tuning; per-screen partial refresh polish.
**Done:** v1 feature set complete per B3.

## Stage 9 — Hardening & handover
**Build:** CloudWatch dashboards + alarms wired to email; device log rotation; backup/export (nightly DynamoDB → S3 JSON); runbooks in /docs (re-flash device, restore data, rotate keys, firmware-update procedure = "don't, and here's why"); final `CLAUDE.md` refresh.
**Open-source release work:** README with screenshots of the board + capture pipeline, architecture diagram, "deploy your own" guide (fork → OIDC role → cdk bootstrap → device setup), `.github/workflows/release-device.yml` publishing the linux-arm device binary as a GitHub Release artifact, CONTRIBUTING.md, issue templates, topics/tags on the repo (`remarkable`, `e-ink`, `adhd`, `dotnet`, `aws-cdk`).

**SECURITY.md + isolation documentation (explicit goal: code is public, Dan's live infrastructure is not usable by anyone else):**
- **`SECURITY.md`** stating plainly: this repo contains no secrets and no live endpoints; forking the code does not grant access to the maintainer's AWS account, DynamoDB, or Anthropic usage; the OIDC trust policy (Checkpoint 1.1) restricts deploy access to `repo:danjourno-dev/anchor:*` only, so a fork cannot deploy into or read from the original AWS account under any circumstances; how to report a genuine vulnerability (email, not a public issue).
- **README "Deploying your own instance" section**, written for a stranger forking the repo, covering what they must bring themselves: their own AWS account + `cdk bootstrap`, their own OIDC provider/trust policy pointed at their fork, their own Anthropic API key in their own SSM, their own device paired to their own API. Make explicit that there is no shared backend, no multi-tenant mode, and no signup flow in v1 — every deployment is fully independent.
- **Threat-model note in the architecture doc:** the single-user API-key model (Stage 2) means the API surface is public knowledge but unusable without a key that's never committed; confirm this stays true in Stage 6/7 (web app auth, capture pipeline) — flag to Dan if any future stage would need this reviewed (e.g. adding a signup flow later).
**⏸ CHECKPOINT 9.1:** disaster drill — restore seed from backup, redeploy device binary from clean SSH.
**⏸ CHECKPOINT 9.2:** secret sweep before announcing — run `gitleaks` (or `git log -p | grep` for key patterns) over full history; confirm no AWS account IDs, API keys, or the device SSH password ever landed in a commit. If any did, they must be rotated, not just deleted.
**Done:** project maintainable by future sessions from docs alone; repo safe and useful for strangers.

---

# PART D — BACKLOG SEED (Claude Code: create in Stage 0)
**Milestones** = Stages 0–9. Under each, create **GitHub Issues** from the Build bullets above, labelled by area (`device`, `backend`, `web`, `infra`, `docs`). Add a `v2` milestone containing: **two-way calendar write-back** (explicitly out of scope for v1 — Anchor is read-only against external calendars by design), Cognito auth, medication/reminder nudges via phone push, weekly-review screen, Kindle wall display as a second read-only client.
Mark a handful of self-contained issues `good first issue` — it's a public repo and that's how strangers start contributing.
