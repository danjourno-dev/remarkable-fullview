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

**Stage 3 — Device hello-world — done.** Checkpoint 3.1 (device prep:
Vellum, AppLoad) was already done by Dan ahead of Stage 3. Session 7 built
the fb0/mxcfb P/Invoke blitter, the hello-world ImageSharp render, and the
publish/deploy scripts. Checkpoint 3.2 confirmed it works on the real
device over WiFi: "HELLO DAN" and the border rendered correctly, right way
up, first try — see "Next up" for the full writeup. Stage 4 (local-first
device app) is next.

**Stage 4 — Local-first device app — done.** Session 8 built the SQLite
store + migrations, all six rendering screens with region-map hit-testing,
the Now/Next strip's mode toggle, evdev tap-to-complete, edge navigation,
and seed data for both contexts. Session 9 reworked the UI to match
`docs/mockup-v4.png`; Session 10 fixed two visual-QA bugs; Session 11
migrated the launcher to AppLoad/qtfb. Checkpoint 4.1 passed on device.
Remaining performance polish is deferred to a later session rather than
blocking Stage 5.

**Stage 5 — Device sync engine — code complete, Checkpoint 5.1 pending.**
Session 12 built outbox drain + delta apply against `/sync`, the
`fullview-sync.timer` systemd unit for headless background sync, and the
footer's tappable "synced HH:MM · n pending" status (see Session 12 log and
Decisions below). All builds/tests/format green on the dev machine — **not
yet run on the device.** Checkpoint 5.1 (kill-WiFi test) is the human step
still needed to close Stage 5.

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

### 2026-07-11 — Session 7 (Stage 3, code)

- Built the fb0/mxcfb P/Invoke layer: `src/Fullview.Device/Native/Fb.cs`
  (open/close/mmap/munmap/ioctl bindings, ioctl request numbers, struct
  byte offsets) and `src/Fullview.Device/FramebufferDevice.cs` (queries
  real geometry via `FBIOGET_VSCREENINFO`/`FBIOGET_FSCREENINFO` instead of
  hardcoding 1404x1872, mmaps `smem_len` bytes, writes an `Image<L8>` as
  either RGB565 (16bpp, the rM1's expected mode) or packed 8bpp grayscale,
  and drives `MXCFB_SEND_UPDATE` for the e-ink redraw).
- Added `src/Fullview.Rendering/BitmapFont.cs` + `HelloWorldScreen.cs`: a
  hand-authored 5x7 block font (no external TTF, so no font-licensing
  question for a public repo) rendering "HELLO DAN" centered inside a
  black border, sized to whatever framebuffer geometry is passed in.
- Added `SixLabors.ImageSharp` (3.1.12 — bumped from the first-tried 3.1.5
  after `dotnet build` flagged two known advisories on 3.1.5; 3.1.12 was
  already in the local NuGet cache and clean) as `Fullview.Rendering`'s
  only new dependency.
- Wired `src/Fullview.Device/Program.cs`: opens the framebuffer, renders
  hello-world at the device's actual reported geometry, writes it, requests
  a full refresh, then stays alive 60s (Ctrl+C to exit early) so Checkpoint
  3.2 has a window to inspect `ps`/`free` before the process exits.
- Added `tools/device/publish-arm.sh` (self-contained linux-arm publish to
  `artifacts/device/`, gitignored) and `tools/device/deploy-over-ssh.sh`
  (scp + remote chmod/run, host/user/path all env-var configurable,
  defaulting to the reMarkable's standard USB IP `10.11.99.1` and user
  `root` — no device specifics committed).
- Tests: `tests/Fullview.Rendering.Tests` gained `HelloWorldScreenTests`
  and `BitmapFontTests` (pure ImageSharp, run on any OS/CI).
  `tests/Fullview.Device.Tests` gained `MxcfbIoctlNumberTests`, which
  re-derives `MXCFB_SEND_UPDATE` from the standard Linux `_IOW` macro and
  asserts it matches the hardcoded constant in `Fb.cs` — this is the one
  piece of the P/Invoke layer that's checkable without real hardware.
  Added `InternalsVisibleTo` on `Fullview.Device` so the test can see
  `Native.Fb`.
- Verified on the dev machine (Windows): `dotnet build Fullview.sln` (0
  warnings), `dotnet format Fullview.sln --verify-no-changes` (clean),
  `dotnet test Fullview.sln --filter "Category!=Integration"` (all pass:
  3 Domain + 6 Rendering + 5 Api + 1 Device), and
  `bash tools/device/publish-arm.sh` (produces a self-contained ~63MB
  32-bit ARM ELF binary at `artifacts/device/Fullview.Device`). **None of
  this proves the fb0/mxcfb code actually works** — that only happens on
  the device itself, which is Checkpoint 3.2, not yet run this session.

### 2026-07-11 — Session 8 (Stage 4, code)

- Extended `src/Fullview.Rendering/BitmapFont.cs`'s glyph set from 8
  characters to full A-Z, 0-9, and the punctuation the screens actually
  need (`: . , - / ! + ' ( )`).
- Added the layout primitive layer under `src/Fullview.Rendering/Layout/`:
  `BoardAction` (closed hierarchy: `ToggleMode`, `NavigatePrevious/Next`,
  `ToggleTodo`, `ToggleShoppingItem`, `OpenRecipe`), `HitRegion`,
  `ScreenRenderResult`, `ScreenKind`, `ScreenSet` (Personal vs Work
  navigation order + wraparound `Next`/`Previous`), `Canvas` (shared
  fill-rect/strikethrough helpers), `NowNextStrip`, `Footer`, `ListPage`
  (pagination), `NowNextCalculator` (pure — merges both contexts' agendas
  cross-context per B3, picks current/next timed event), `BoardState`
  (the full per-frame render input, with `WithMode`/`WithScreen`/
  `WithOpenRecipe` helpers), and `BoardRenderer` (composes strip + body +
  footer + edge-nav hit zones into one frame).
- Added the six screens under `src/Fullview.Rendering/Screens/`: Today,
  Todos, Agenda, Meals, Shopping, Recipe — each a static `Render(...)`
  returning body-local `ScreenRenderResult`s that `BoardRenderer` offsets
  into board-global coordinates.
- Added the device-local SQLite layer under `src/Fullview.Device/Storage/`:
  `Migrations` (v1: `entities`/`outbox`/`settings` tables), `DeviceDatabase`
  (owns the connection, applies pending migrations via `PRAGMA
  user_version`), `DeviceJson` (reuses the exact `JsonSerializerDefaults.Web`
  convention `Fullview.Api.Sync.SyncJson` uses server-side, so device/server
  JSON shapes match), `DeviceStore` (`Query<T>`, `Save` — entity row +
  outbox row in one transaction per B5, `SaveSeed` — entity row only,
  `ToggleTodoCompleted`, `ToggleShoppingItemChecked`), and `DeviceSettings`
  (device-local current-mode setting, defaults to Personal).
- Added `src/Fullview.Device/Storage/SeedData.cs`: fabricated demo data for
  both contexts (todos, agenda events, meals, shopping items, one recipe),
  applied only when the store is empty so it never clobbers real data.
- Added touch input under `src/Fullview.Device/Input/`: `RawInputEvent` +
  `EvCodes` (decoded evdev event + the type/code constants this app reads),
  `TouchTapDetector` (pure, clock-free — uses each event's own kernel
  timestamp, not wall-clock, so it's testable from a canned sequence; a tap
  is ABS_MT_TRACKING_ID assigned-then-cleared within 400ms and 40 touch-units
  of movement) and `EvdevTouchDevice` (P/Invoke read loop over
  `/dev/input/eventN`, hardware only). Originally built against BTN_TOUCH per
  Dan's steer toward simple single-touch polling, but Checkpoint 4.1 on real
  hardware showed `cyttsp5_mt` reports `KEY=0` — no EV_KEY codes at all, so
  BTN_TOUCH never fires. Switched down/up detection to ABS_MT_TRACKING_ID
  (still only last-known ABS_MT_POSITION_X/Y, no full multi-slot tracking).
- Extended `FramebufferDevice`: `Refresh(bool)` now delegates to a shared
  `SendUpdate`, and a new `RefreshRegion(Rectangle)` does a partial update
  with the fast monochrome (DU) waveform — used for tap-to-complete/
  mode-toggle instead of the slower full-panel GC16 refresh.
- Wired `src/Fullview.Device/Program.cs`: opens the DB (seeding if empty)
  and the framebuffer, renders the initial board with a full refresh, then
  loops reading touch events, hit-testing taps against the last render's
  regions, applying the resulting `BoardAction` to the store/state, and
  doing a targeted `RefreshRegion` (the tapped hit region for todo/shopping
  toggles, the whole panel for mode/navigation/recipe changes, since those
  redraw the whole body).
- Added `tools/device/fullview-device.service` (systemd unit, runs the
  binary from `/home/root`, `Restart=on-failure`) and
  `docs/device-setup.md` (build/deploy/systemd-install steps and the two
  runtime env vars, `FULLVIEW_DB_PATH`/`FULLVIEW_TOUCH_DEVICE`, written for
  a stranger forking the repo).
- Tests: 13 new `Fullview.Rendering.Tests` (ScreenSet, NowNextCalculator,
  BoardRenderer) + 1 BitmapFont full-charset test (20 total, all pass); 11
  new `Fullview.Device.Tests` (DeviceStore, DeviceSettings) + 6
  `TouchTapDetectorTests` (17 total, all pass). Full solution: `dotnet
  build`/`dotnet test`/`dotnet format --verify-no-changes` all green.
  **None of this proves the device-side code (SQLite on the real
  filesystem, the touch device path, partial e-ink refresh) works on
  hardware** — that's Checkpoint 4.1, not yet run.

### 2026-07-11 — Session 9 (Stage 4, UI rework to mockup v4)

- Dan saved `docs/mockup-v4.png` and asked for two things: (1) drop the
  tappable PERSONAL/WORK badge in favor of the reMarkable 1's physical
  bottom-right hardware button for mode switching, with the footer's old
  "not yet synced" text replaced by a hint that explains the button; (2)
  make the whole board match the mockup's look. Confirmed scope with Dan
  before starting — the mockup implies a full visual redesign (new title
  header, double-ruled boxes everywhere, a 4-panel Today dashboard, and
  Work-mode-only "WAITING ON"/"SHUTDOWN" panels), not just the footer.
- **Header (new):** `src/Fullview.Rendering/Layout/Header.cs` — a title bar
  above the strip on every screen: "LIFE OPS"/"WORK OPS" + a date/inbox
  subtitle, double-ruled box.
- **Canvas.DrawFrame (new):** the mockup's double-ruled box style (outer
  border, inset second border) used by Header, NowNextStrip, Footer, and
  every Today panel — one shared primitive instead of each component
  drawing its own border.
- **NowNextStrip:** dropped the PERSONAL/WORK label it used to draw at its
  own top-right (mockup v4 doesn't show a mode badge in the strip at all —
  mode now only appears in Header/Footer); wrapped in `Canvas.DrawFrame`;
  `Draw` now takes an `originY` so it can sit below Header instead of
  always starting at row 0.
- **Footer:** removed the tappable PERSONAL/WORK badge and its `ToggleMode`
  hit region entirely — `Footer.Draw` no longer returns a `HitRegion`.
  Replaced the screen-name-left/sync-status-right layout with
  `INBOX: <status> // HW BUTTON = SWITCH MODE` on the left and the plain
  (non-tappable) mode name on the right, wrapped in `Canvas.DrawFrame`.
  Removed `BoardState.SyncStatus` — the literal "not yet synced" string it
  held is gone, nothing renders it now (Stage 5's real sync engine can add
  a real status surface back later if needed).
- **TodayScreen — rewritten as a 4-panel dashboard** (was a single-column
  agenda/focus-todos/one-line-summary list): 2x2 double-ruled panels —
  Agenda + (Meals in Personal / Waiting On in Work) on top, Reminders +
  (Shopping in Personal / Shutdown in Work) on the bottom. Every panel ends
  in a `[ TAP TO OPEN ]` hit region (new `BoardAction.NavigateToScreen`)
  that jumps straight to the matching full screen, the same way `OpenRecipe`
  already bypasses `ScreenSet`'s nav order. Todo-backed panels
  (Reminders/Waiting On/Shutdown) also get a per-row `ToggleTodo` hit region
  above the hint line, reusing `TodosScreen`'s tap-to-complete pattern.
- **Reminders panel is cross-context**, like `NowNextStrip`'s Now/Next: it
  shows WORK and PERSONAL subsections regardless of the board's current
  mode, sourced from `BoardState.Todos` (the full unfiltered snapshot)
  rather than the mode-filtered list `BoardRenderer` normally hands to
  screens.
- **Asked Dan how to source Waiting On / Shutdown**, since the mockup's
  content for those two (owner-tagged blocked items; a routine-style
  shutdown checklist) doesn't map to any entity that exists yet — and
  `ScreenKind.cs` explicitly defers Routine/RoutineCheck to Stage 8
  ("Routines are v1.5 ... have no screen yet"). Dan chose: repurpose
  Todos rather than pull Routine forward or ship empty placeholders.
  **Waiting On** = incomplete Work todos with `Priority == Focus`.
  **Shutdown** = incomplete Work todos with any other priority. No owner
  tags (Todo has no such field) — revisit if/when Stage 8 gives these
  panels real backing entities.
- **Hardware button wiring:** renamed `EvdevTouchDevice` →
  `Fullview.Device.Input.EvdevDevice` (it was already a generic
  raw-input-event reader, not touch-specific — the old name just predated
  a second use). Added `EvCodes.EV_KEY`/`KEY_RIGHT` and
  `Evdev.DefaultButtonDevicePath` (`/dev/input/event1`, the same gpio-keys
  node Session 8 already identified for the physical buttons — see Known
  issues there). **`KEY_RIGHT` = 106 is the standard
  linux/input-event-codes.h value and is unverified on real hardware** —
  same caveat class as the Session 7 mxcfb ioctl constant; if the button
  doesn't switch mode at Checkpoint 4.1, check the real code with `evtest`
  and fix `RawInputEvent.cs` (documented in `docs/device-setup.md`).
- **`Program.cs` restructured from one blocking read loop to two producer
  threads + one consumer.** Reading both the touch device and the button
  device requires two concurrent blocking `read()`s; a background thread
  per device now decodes raw events and pushes a small `DeviceInput`
  (tap-with-coordinates, or a bare hardware-button marker) onto a shared
  `BlockingCollection`. The main thread is the sole consumer — it's still
  the only place `BoardState`/`lastRender` mutate, so no locking needed
  there. Hardware-button inputs skip hit-testing and dispatch
  `BoardAction.ToggleMode` directly; tap inputs hit-test against
  `lastRender.Regions` exactly as before.
- Verified locally: `dotnet build`, `dotnet test --filter
  "Category!=Integration"`, and `dotnet format --verify-no-changes` all
  pass. Updated `BoardRendererTests` (dropped `SyncStatus` from the test
  fixture, replaced the old "mode badge exists" assertion with one
  confirming no tap target exists for `ToggleMode`, added a test that
  Today's panels carry `NavigateToScreen` hits) and
  `docs/device-setup.md` (new `FULLVIEW_BUTTON_DEVICE` env var, `evtest`
  troubleshooting note for the unverified key code).
- **Not verified on real hardware yet** — this rides on top of Stage 4's
  existing not-yet-verified-on-device state, plus two brand-new unknowns:
  whether `KEY_RIGHT` is actually what the right-hand button reports, and
  whether reading two evdev devices concurrently from background threads
  behaves as expected on the device's kernel/libc. Both need Checkpoint
  4.1.

### 2026-07-12 — Session 10 (Stage 4, UI polish + visual QA)

- Session 9's mockup rework had never actually been rendered and looked at —
  only build/test/format were checked. This session added a throwaway xUnit
  test (`_PreviewRenderTemp.cs`, deleted before finishing — not committed)
  that calls `BoardRenderer.Render` directly with hand-built seed-shaped
  data and saves the result as a PNG, so the real rendering pipeline could
  be eyeballed against `docs/mockup-v4.png` without needing the device.
  Worth recreating the same way for future UI sessions rather than trusting
  green tests alone — this caught two real bugs green tests missed:
- **Bug: Shopping panel on the Today dashboard was a dead end.** It drew
  "SHOPPING — N ITEMS" but never listed the items or gave them a tap
  target, unlike the mockup's checkbox list. Root cause: `TodayScreenData`
  only carried `ShoppingOpenCount` (an int), not the items themselves.
  Fixed by changing that field to `ShoppingItems`
  (`IReadOnlyList<ShoppingItem>`) and adding `TodayScreen.DrawShoppingRow`
  (checkbox + strikethrough + `ToggleShoppingItem` hit region), mirroring
  the existing `DrawTodoRow` pattern. `BoardRenderer.BuildTodayData` now
  passes `Active(state.ShoppingItems)` straight through instead of
  precomputing a count.
- **Bug (pre-existing, not introduced this session): `BitmapFont` had no
  glyphs for `[` or `]`.** Unsupported characters silently render as a
  space (by design, for graceful degradation), so every checkbox (`[X]`/
  `[ ]`) and every panel's `[ TAP TO OPEN ]` hint text across the whole
  app — not just the new Today dashboard — has been rendering without its
  brackets since Session 8 introduced checkboxes. Only became obvious once
  the mockup rework made checkboxes visually central. Added 5x7 glyphs for
  both characters to `Glyphs`, matching the existing `(`/`)` style.
- Re-ran the preview-render test after both fixes and compared side by
  side with the mockup for both modes: header, strip, panel borders,
  checkbox rows, and footer all now match structurally. **This is still
  only a same-resolution PNG rendered on the dev machine — not a real
  device screenshot.** E-ink's actual grayscale/ghosting behavior, and
  whether the 5x7 font is legible at arm's length on the real 1404x1872
  panel, are still unverified — that's part of Checkpoint 4.1, unchanged
  from Session 9's note.
- Verified locally: `dotnet build`, `dotnet test --filter
  "Category!=Integration"` (48 pass before deleting the temp test, 47
  after), `dotnet format --verify-no-changes` all green. Nothing
  committed — left staged for Dan to review.

### 2026-07-12 — Session 11 (Stage 4, font swap + list sizing + reminders filter)

- Dan reported the list styling from Session 10's checkbox rework "still
  not right" and asked for four things: 1.5x bigger list rows, a real
  font (Source Sans 3) across the whole solution replacing the
  hand-authored 5x7 bitmap font, a divider line above/below every list
  row, and Reminders on the Today dashboard filtered by mode (Work
  reminders only in Work Ops, Personal only in Life Ops — previously it
  showed both unconditionally, unlike everything else on the board which
  is mode-scoped).
- **Font:** replaced `BitmapFont` (deleted, along with its test) with a
  new `AppFont` wrapping `SixLabors.Fonts`/`SixLabors.ImageSharp.Drawing`
  (added as a package reference — compatible with the existing
  ImageSharp 3.1.12, no major-version bump needed). Embedded Source Sans 3
  v3.052R (SIL OFL 1.1, official Adobe release) as two `EmbeddedResource`
  TTFs (`Assets/Fonts/SourceSans3-{Regular,Bold}.ttf` + `OFL.txt`) so
  device deploys stay self-contained — no system font dependency. `AppFont`
  keeps the old call shape (`DrawText`, `MeasureWidth`, plus a new
  `LineHeight` standing in for the old `GlyphHeight * scale`) to keep the
  migration mechanical across all 11 call sites (Header, Footer,
  NowNextStrip, HelloWorldScreen, every screen, BoardRenderer's inline
  fallback text).
- **1.5x rows + dividers:** scoped this to the screens that are actually
  checkbox/tap-target list rows — Todos, Shopping, Agenda, and the
  Today dashboard's Todo/Shopping panel rows. Meals and Recipe screens got
  the font swap but not the 1.5x/divider treatment (they're not
  checkbox-style list rows and weren't part of Session 10's changes
  either) — flag if Dan wants those screens to match too.
  `Canvas.DrawDivider` added as a thin `FillRect` helper. Row heights
  went 60-84px → 90-126px depending on screen; checkbox/text gap and hit
  region padding scaled with them so tap targets stay accurate.
- **Reminders mode filter:** `TodayScreenData` still carries both
  `WorkReminders`/`PersonalReminders` unfiltered (BoardRenderer's job is
  building the data, not filtering for display); `TodayScreen.Render` now
  picks whichever list matches `data.Mode` before handing it to the
  existing `DrawTodoPanel`. Updated the stale doc comments in both files
  that used to say Reminders was intentionally cross-context like
  NowNextStrip's Now/Next — it isn't anymore.
- Repeated Session 10's throwaway render-to-PNG visual QA pattern
  (`_PreviewRenderTemp.cs`, deleted before finishing, never committed):
  rendered Today/Todos/Shopping for both modes. Caught one bug: the
  panel-header right-side label ("3 ITEMS", "1 OPEN", "1 LEFT") was still
  sized at the new 32px row font and visually collided with the title's
  divider rule once rows got bigger — fixed by giving panel titles/labels
  their own `PanelLabelFont` (22px) independent of row size.
  `SaveAsPng` needed `using SixLabors.ImageSharp;`, not
  `SixLabors.ImageSharp.Formats.Png` — the extension method lives in the
  root namespace.
- Verified locally: `dotnet build` (0 warnings/errors), `dotnet test
  --filter "Category!=Integration"` (all 43 pass — 3 Domain + 5 Api + 18
  Rendering + 17 Device), `dotnet format --verify-no-changes` clean.
  Nothing committed — left staged for Dan to review, per standing
  instruction never to commit on his behalf.

### 2026-07-12 — Session 12 (Stage 4, tap-responsiveness perf pass)

- Dan captured real-device debug logs (the `[debug] Render breakdown` /
  `Timing:` lines already wired up in `Program.cs`/`RenderDiagnostics`)
  after tapping a reminders-panel checkbox and asked for ideas to cut the
  tap-to-refresh latency, which ran 560-965ms `total-app`.
- **Root-caused two bottlenecks from the logs, not guesswork:**
  `fillRect`/`Composite` calls were already near-free (79-106 calls in
  under 2ms), which pointed straight at the other two breakdown numbers:
  - **Text rendering (~45-90ms per `DrawText` call, the dominant cost).**
    `AppFont.DrawText` had no glyph cache — every call re-ran SixLabors'
    outline-to-raster fill + antialiasing from scratch, even for
    characters/fonts repeated across nearly every render.
  - **Framebuffer blit (105-455ms, climbing across taps rather than
    settling).** `WriteImageRgb565`/`WriteImageGray8` called
    `Marshal.WriteInt16`/`WriteByte` once per pixel — ~2.6M P/Invoke-style
    calls for a full 1404x1872 frame.
  - (`db/apply` spiking to 536ms was a one-time cold SQLite open, not a
    per-tap cost — left alone.)
- **Fix 1 — glyph cache in `AppFont`:** each unique `(font, char)` is now
  rasterized once (via the same SixLabors `DrawText` path as before, onto
  a scratch canvas) into a cropped ink-coverage mask, cached, and reused
  via a cheap per-pixel coverage blend on every subsequent draw instead of
  re-rasterizing. Per-character advance widths come from
  `TextMeasurer.MeasureAdvance` on each character in isolation — there's
  no cross-glyph kerning in the cached path (see Decisions).
- **Fix 2 — blit via row buffers + a lookup table in
  `FramebufferDevice`:** `WriteImageRgb565`/`WriteImageGray8` now build
  each row into a reused managed `byte[]` and `Marshal.Copy` the whole row
  in one call (~1872 calls instead of ~2.6M), and the gray→RGB565
  conversion is a precomputed 256-entry table instead of per-pixel
  shifts.
- **Fix 3 — panel chrome cache in `TodayScreen`, at Dan's request:** the
  panel frame/title/rule/"[ TAP TO OPEN ]" hint never changes when only a
  row's checked state does, so it's now cached separately (`ChromeCache`,
  keyed by title/hasHint/size) from the existing row-content
  `PanelCache`. A single-row toggle composites the cached chrome instead
  of re-drawing the title text.
- Verified: `dotnet build`, `dotnet test`, and
  `dotnet format --verify-no-changes` all green. Dan re-captured the same
  device debug logs after the fix: `total-app` dropped from 560-965ms to
  **227ms**; `text` dropped from 45-90ms/call to **~3.8ms/call**; `blit`
  dropped from the 250-455ms range to **~132ms** — now the largest single
  remaining line item.
- **Open for next session:** blit (~132ms) is still the biggest cost.
  Worth investigating whether the mmap'd fb0 write needs an explicit
  `msync`/cache-flush that's adding overhead, or whether the
  `RefreshRegion` ioctl call can be batched/overlapped with the write —
  not yet investigated. Nothing committed — left staged for Dan to review.

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
- **Stage 3 — framebuffer geometry is queried at runtime, not hardcoded.**
  `FramebufferDevice.Open()` calls `FBIOGET_VSCREENINFO`/`FBIOGET_FSCREENINFO`
  rather than assuming the known 1404x1872/16bpp numbers, so a wrong
  assumption fails loudly (`IOException`) instead of silently mis-rendering.
  Struct offsets are for **armhf (32-bit `unsigned long`)** specifically —
  this code will never run anywhere else, so no cross-arch handling was
  added.
- **Stage 3 — `MXCFB_SEND_UPDATE` = `0x4048462e`, the EPDC v2
  `mxcfb_update_data` layout (72 bytes: adds `dither_mode`/`quant_bit`
  before `alt_buffer_data`), matching libremarkable and the rest of the rM
  homebrew ecosystem, per the plan's "reference libremarkable struct
  layout" instruction. `tests/Fullview.Device.Tests/MxcfbIoctlNumberTests`
  re-derives this from the `_IOW` macro so the constant isn't just a magic
  number pasted from a comment. **This is unverified on real hardware
  until Checkpoint 3.2** — if the ioctl fails there, `FramebufferDevice`
  is written to log the errno rather than throw (pixels are still in the
  mmap'd frame either way), so a wrong refresh call won't crash the
  process, but it does mean "process ran without error" isn't sufficient
  proof — Dan needs to actually look at the screen.
- **Stage 3 — no font asset shipped; hand-authored 5x7 bitmap font
  instead.** Avoids a font-licensing question in a public repo and a
  dependency on `SixLabors.Fonts` for what's a one-off derisking screen.
  Stage 4's real screens ("Fullview.Rendering screens... region-map
  hit-testing") will need actual typography and should pick a proper
  open-license font then, not reuse this.
- **Stage 3 — device deploy config is env vars, not a committed file.**
  `tools/device/deploy-over-ssh.sh` takes `DEVICE_HOST`/`DEVICE_USER`/
  `DEVICE_PATH` as env vars (defaulting to the reMarkable's well-known
  USB IP `10.11.99.1` and user `root`, which aren't secrets), matching how
  `tools/seed-data` already avoids committing Dan-specific config.
- **Stage 4 — kept the hand-authored bitmap font rather than switching to
  a real typeface as Session 7's decision suggested.** Extending the
  existing 5x7 font to the full alphanumeric+punctuation set the six
  screens need was a small, contained change with no new dependency or
  licensing question; revisit if Checkpoint 4.1 feedback says legibility
  is a real problem, not before.
- **Stage 4 — outbox built now, not deferred to Stage 5.** B5 frames
  "outbox row in the same transaction as every local write" as a
  whole-app invariant, not something scoped to Stage 5's Build bullet
  (which only owns drain/connectivity/retry). `DeviceStore.Save` writes
  both rows transactionally today; `SaveSeed` deliberately skips the
  outbox since seed rows aren't real mutations to sync.
- **Stage 4 — DB path and touch device path are env-var overridable, not
  hardcoded.** `FULLVIEW_DB_PATH` defaults to a file next to the binary
  (`AppContext.BaseDirectory`); `FULLVIEW_TOUCH_DEVICE` defaults to
  `/dev/input/event2`. Both can be overridden without a rebuild if
  Checkpoint 4.1 finds the defaults wrong — see Known issues below for
  the touch device path specifically.
- **Stage 4 — partial refresh uses the DU waveform, full refresh keeps
  GC16.** `FramebufferDevice.RefreshRegion` is for tap-to-complete/
  mode-toggle: fast, monochrome-only, no grayscale ghost cleanup — fine
  since `BoardRenderer` only ever draws black/white. The original
  `Refresh(fullRefresh: true)` path (GC16, full panel) is unchanged for
  the initial boot render.
- **Stage 4 — `AppFont`'s glyph cache lays out cached glyphs using each
  character's own advance width, with no cross-glyph kerning.** Accepted
  the small spacing inaccuracy (a handful of specific letter pairs may sit
  a pixel or two off from SixLabors' fully-kerned layout) in exchange for
  turning the per-`DrawText`-call cost from tens of ms of outline
  rasterization into a cache lookup + blit — the actual bottleneck on the
  rM1's CPU. Revisit only if a specific rendered pair looks visibly wrong
  on device.
- **Stage 5 — three fixed sync trigger points, no background ticker.**
  Rejected a `SyncTicker` thread with its own backoff loop inside the
  foreground app, in favor of: startup sync (always attempted, short
  timeout, failure just leaves the cache stale rather than blocking the
  UI), a manual tap on the footer's sync status, and a headless systemd
  timer for periodic background drain. Simpler to reason about than a
  long-lived background thread sharing the SQLite connection with the
  render loop, and matches Dan's explicit requirement that the periodic
  timer shouldn't touch the network at all unless the outbox is non-empty.
- **Stage 5 — systemd is back, scoped to a oneshot headless mode.**
  `fullview-sync.service`/`.timer` run the same binary with
  `FULLVIEW_MODE=sync-once`, which returns before opening `/dev/fb0`,
  qtfb, or evdev — unlike the deleted `fullview-device.service` (Session
  11), this can never contend with AppLoad for the framebuffer, so
  reintroducing systemd here doesn't reopen that problem.
- **Stage 5 — WAL + `busy_timeout` instead of a cross-process lock file.**
  The headless `sync-once` process and the foreground app can both hold
  `fullview.db` open at once (the timer can fire while the app is open).
  WAL journal mode lets readers and a writer coexist instead of failing on
  `SQLITE_BUSY`; a 5s `busy_timeout` covers writer-vs-writer contention by
  retrying instead of throwing. Simpler than adding an app-level lock file
  or IPC between the two processes.
- **Stage 5 — footer hint text replaced, not appended.** The "HW BUTTON =
  SWITCH MODE" hint became the tappable "synced HH:MM · n pending" status
  (Dan's choice) rather than adding a second line — keeps the footer's
  height and the render diff/cache-key logic unchanged.
- **Stage 5 — `/sync` still has no auth.** Unchanged from the Stage 2
  decision (see above); `SyncClient` sends no auth header. Out of scope
  for Stage 5.

## Known issues / blockers

- **Stage 4 — resolved: touch device path was wrong, now confirmed.**
  Checkpoint 4.1 on real hardware showed taps not registering.
  `cat /proc/bus/input/devices` revealed `/dev/input/event1` is
  `gpio-keys` (the physical side buttons), not the touchscreen; the
  capacitive touch controller (`cyttsp5_mt`) is `/dev/input/event2`.
  Updated the default in `Evdev.cs` and docs/device-setup.md
  accordingly. Still overridable via `FULLVIEW_TOUCH_DEVICE` if a
  different unit reports a different `eventN`.
- The `Dan-613` keyring account shown by `gh auth status` is
  stale/inactive and harmless — the active account is `danjourno-dev`.

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
  deployed stack.
- **Checkpoint 3.2 — done.** Dan ran `tools/device/publish-arm.sh` then
  `tools/device/deploy-over-ssh.sh` (over WiFi, `DEVICE_HOST=192.168.178.48`
  via Git Bash directly — `bash` on PATH in a plain PowerShell prompt
  resolves to the WSL launcher, not Git Bash, and has no distro installed;
  invoking `"C:\Program Files\Git\bin\bash.exe"` explicitly sidesteps
  that). Confirmed on the actual device: **"HELLO DAN" rendered correctly
  — right-side up, not mirrored/rotated — and the black border showed on
  all four edges.** So both the fb0 mmap write path and the
  `MXCFB_SEND_UPDATE` ioctl (the two big unknowns from Session 7) work as
  written, first try, no rotation-handling needed after all.
  **RSS/`free` not captured** — the first attempt raced the 60s window
  (the `ps` ran before the app had started) and Dan didn't re-run a second
  time. Not worth chasing now: Stage 4 makes this a long-running systemd
  service, which is a far easier target to measure RSS against than a
  one-shot 60s process. Revisit there if memory footprint becomes a real
  question.
- **Stage 3 Done criteria met** ("pixels on e-ink from our binary, deploy
  script repeatable" — both true). **Stage 3 is complete.** Next session:
  start Stage 4 (local-first device app — SQLite store, region-map
  screens, mode badge, tap-to-complete, systemd unit).
- **Stage 4 code complete this session (Session 8) — all Build bullets
  done, all tests/build/format green on the dev machine, nothing yet
  verified on the device.** Remaining before Stage 4 is genuinely closed:
  **Checkpoint 4.1** — deploy to the device (`tools/device/publish-arm.sh`
  + `tools/device/deploy-over-ssh.sh`, then install
  `tools/device/fullview-device.service` per docs/device-setup.md), confirm
  the seeded board renders correctly, confirm taps register (touch device
  path may need overriding — see Known issues), and Dan lives with it for
  a day before filing layout/type-size feedback as GitHub Issues. Once
  that's done, Stage 4's Done criteria ("fully offline interactive
  dashboard on device") is fully met and Stage 5 (device sync engine) is
  next.
- **Session 9 reworked the UI to match `docs/mockup-v4.png`** (new Header
  bar, double-ruled panels, 4-panel Today dashboard, hardware-button mode
  switch — see Session 9 log and Decisions) before Checkpoint 4.1 runs.
  All still-open Checkpoint 4.1 items above apply, plus two new unknowns
  this session added: whether `EvCodes.KEY_RIGHT` (106) is really what the
  right-hand physical button reports on `/dev/input/event1`, and whether
  the touch+button dual-thread evdev read loop behaves correctly on
  device. If the button doesn't switch mode, see the `evtest`
  troubleshooting note in `docs/device-setup.md`.
- **Session 10 did a visual QA pass on Session 9's rework** (render-to-PNG
  comparison against the mockup — see Session 10 log) and fixed two bugs
  that green tests hadn't caught: the Today dashboard's Shopping panel
  wasn't listing/toggling items, and `BitmapFont` was silently dropping
  `[`/`]` from every checkbox and "[ TAP TO OPEN ]" hint app-wide. **Next
  session: continue UI polish this same way** — recreate the throwaway
  render-to-PNG test (see Session 10 log for the pattern), compare fresh
  screenshots against `docs/mockup-v4.png` for every screen (not just
  Today — Agenda/Meals/Shopping/Todos/Recipe full-screen views haven't had
  this treatment yet), and look for other silently-dropped characters or
  layout gaps the same way. Checkpoint 4.1 (real device) is still the
  final gate before Stage 4 is closed, but doing another round or two of
  dev-machine visual QA first will surface cheaper fixes before that trip.

### 2026-07-12 — Session 11 (AppLoad launcher migration)

- Dan had been hand-launching the app over SSH, which fights `xochitl` for
  both the framebuffer and the input devices. Preflight over SSH confirmed
  AppLoad (asivery/rm-appload) and its qt-resource-rebuilder dependency are
  already installed under `/home/root/xovi/` and **do run on this rM1**
  (README's `aspectRatio: "original"` explicitly covers rM1/rM2/rMPP) —
  `/home/root/xovi/exthome/appload/` didn't exist yet (no apps installed),
  and `fullview-device.service` had never actually been deployed
  (`not-found`/`inactive`), though a stray hand-launched process was running.
- Rather than ship a raw-fb0 `qtfb: false` staging step, Dan chose to
  implement a real qtfb client first, so the app renders into AppLoad's
  shared surface instead of fighting xochitl for `/dev/fb0`. Built from
  reading AppLoad's own source (`src/qtfb/`): an `IScreen` abstraction
  (`FramebufferDevice` and the new `QtfbScreen` both implement it),
  `QtfbScreen` (Native/`Qtfb.cs` P/Invoke — AF_UNIX SOCK_SEQPACKET socket at
  `/tmp/qtfb.sock`, 24-byte messages, shared-memory RGB565 surface at
  `/dev/shm/qtfb_<key>`), and `QtfbInputSource` (reads MESSAGE_USERINPUT off
  the same connection, already in screen pixel coordinates — no
  767x1023 rescale or 180° flip like the raw digitizer needs).
  `Program.cs` picks the qtfb path when `QTFB_KEY` is set (i.e. launched by
  AppLoad) and falls back to the original `FramebufferDevice` + evdev path
  otherwise (hand-launch over SSH still works for local debugging).
  Extracted the gray8→RGB565 lookup table into `Rgb565.cs`, shared by both
  screens (previously private to `FramebufferDevice`).
- Deleted `tools/device/fullview-device.service`: an auto-restarting
  background service that owns the framebuffer is fundamentally incompatible
  with a launcher-managed app. Rewrote `deploy-over-ssh.sh` to install into
  `/home/root/xovi/exthome/appload/fullview/` (AppLoad id
  `external::fullview`) instead of `/home/root/fullview-device-dir`, disabling
  any leftover systemd unit first. Added
  `tools/device/appload/external.manifest.json` (`qtfb: true,
  disablesWindowedMode: true, aspectRatio: "original"`) and a generated "FV"
  monogram placeholder `icon.png`. Updated `docs/device-setup.md` to match.
- All three CI commands green (build/test/format) with new tests for the
  qtfb message encoding (`QtfbMessageTests`), the RGB565 table
  (`Rgb565Tests`), and the input press/release → tap mapping
  (`QtfbInputSourceTests`, using a scripted `Func<QtfbUserInput>` feed rather
  than a live socket, since `dotnet test` runs on Windows and can't load
  `libc`).
- **Not yet run on the device.** Next: `publish-arm.sh` +
  `deploy-over-ssh.sh`, launch "Fullview" from the AppLoad launcher, confirm
  the board renders without overdraw/contention with xochitl, and — the key
  open question — confirm xochitl actually forwards touch input to the qtfb
  client on rM1 (tap a checkbox). If it doesn't, the documented fallback is
  to keep reading evdev directly even under qtfb (render via qtfb, input via
  evdev); the `IScreen`/input-source split supports mixing them. Whether
  physical hardware buttons arrive as qtfb `INPUT_BTN_*` events at all is a
  second, separate unknown — noted in `QtfbInputSource` but not resolved.

### 2026-07-12 — Session 12 (Stage 5, device sync engine)

- Dan confirmed Stage 4 is functionally done (performance polish deferred)
  and to proceed to Stage 5. Built `SyncClient` (thin `POST /sync` wrapper,
  no auth header — still out of scope per Stage 2's decision) and
  `SyncEngine.SyncOnceAsync` (drain outbox → call `/sync` → apply the
  returned delta with the same LWW rule as `DynamoSyncStore` → advance the
  cursor and `LastSyncedAt`; a failed call leaves the outbox and cursor
  untouched for the next trigger to retry).
- Dropped the originally-planned `SyncTicker`/backoff/`ConnectivityProbe`
  background-thread design after asking Dan how RTC-wake should work. His
  answer set three concrete requirements that became the three sync
  trigger points instead: (1) a headless systemd timer that only makes a
  network call if the outbox has pending writes ("shouldn't read unless
  it's already writing"), to save power; (2) the foreground app always
  syncs fresh on open, so it catches up on anything changed elsewhere while
  it was closed; (3) a manual sync affordance so Dan can force a sync on
  demand. See Decisions below for how each landed.
- Added WAL journal mode + `busy_timeout=5000` PRAGMAs to `DeviceDatabase.Open`
  so the foreground app and a headless `sync-once` process can safely hold
  the SQLite file open at the same time.
- Reintroduced systemd — deliberately narrow this time. Unlike the deleted
  `fullview-device.service` (always-on, owned the framebuffer, fought
  AppLoad), `tools/device/systemd/fullview-sync.{service,timer}` runs the
  binary in `FULLVIEW_MODE=sync-once`, which returns before ever opening
  `/dev/fb0`/qtfb/evdev — it can't contend with the foreground app for
  anything. `deploy-over-ssh.sh` now installs and enables the timer
  (30 min interval, `WakeSystem=true`, `Persistent=true`) alongside its
  existing disable of any leftover old service.
- Footer layout: replaced the "HW BUTTON = SWITCH MODE" hint text with a
  tappable "synced HH:MM · n pending" status (Dan's explicit choice over
  alternatives) — `Footer.Render` now returns the status text's bounds so
  `BoardRenderer` can register a `BoardAction.SyncNow` hit region over it,
  giving the manual-sync affordance from requirement (3) above.
- All three CI commands green (build/test/format) with new tests:
  `SyncEngineTests` (success drain, failed call leaves outbox/cursor
  untouched, empty-outbox-still-applies-delta, LWW-remote-older-doesn't-
  overwrite) using an inline fake `HttpMessageHandler`; new `DeviceStore`
  tests for `ReadOutbox`/`OutboxCount`/`DeleteOutboxThrough`/
  `ApplyRemoteDelta` (both LWW directions); new `DeviceSettings` tests for
  the sync-cursor/last-synced-at getters/setters.
- **Not yet run on the device.** Next: `publish-arm.sh` +
  `deploy-over-ssh.sh` (creating `/etc/fullview-sync.env` on the device
  first — see `docs/device-setup.md`), then Checkpoint 5.1 — tick items
  offline, kill WiFi, confirm the outbox queues; restore WiFi, confirm the
  timer (or a manual sync tap) drains it and the web/API side converges.

### 2026-07-12 — Session 13 (fullview-sync.service TLS bug)

- After deploying Session 12's sync engine, `fullview-sync.service` failed
  every run (`journalctl`) with
  `AuthenticationException: NotTimeValid` on the `POST /sync` TLS handshake
  against `https://vqnmcbnti3.execute-api.eu-west-2.amazonaws.com`.
  Systematically ruled out, in order: device clock/RTC (correct, NTP
  synced, booted 19h earlier — no boot-time race), an expired
  `Baltimore_CyberTrust_Root.pem` in `/etc/ssl/certs` (real, but unrelated
  — not part of AWS's chain), stale `FULLVIEW_API_BASE_URL` config
  (matched exactly), AWS-side edge cert propagation lag (ruled out —
  100% reproducible across 4+ attempts, not transient), a custom cert
  pinning bug in `SyncClient` (none exists, plain `HttpClient`), a stale
  on-disk AIA cert cache under `~/.dotnet/corefx/...` (none found),
  forcing `SSL_CERT_DIR=/etc/ssl/certs` to match what `openssl s_client`
  uses (no effect), and `PublishReadyToRun` R2R-compiled crypto interop on
  32-bit armhf (rebuilt with `-p:PublishReadyToRun=false`, still failed —
  reverted).
- **Root cause found via a temporary `ServerCertificateCustomValidationCallback`
  diagnostic** (`FULLVIEW_TLS_DEBUG=1`, since removed/replaced — see
  below): every certificate in the chain, including the 2015-issued
  Amazon root, failed with chain status `NotTimeValid` /
  `"format error in certificate's notBefore field"` — OpenSSL's literal
  `X509_V_ERR_ERROR_IN_CERT_NOT_BEFORE_FIELD`, a *native ASN.1 parse
  failure*, not an actual date-range failure. .NET's own *managed*
  `X509Certificate2.NotBefore`/`NotAfter` properties parsed every cert
  correctly in the same run. Conclusion: the device's system
  `libssl.so.3`/`libcrypto.so.3` (OpenSSL 3.2.6, armv7l) has a native
  `time_t`-handling bug in its cert-chain time check — likely a 32-bit ARM
  `time_t` ABI mismatch from the glibc Y2038 64-bit `time_t` transition —
  that corrupts `ASN1_TIME_to_tm` for every cert regardless of validity.
  `openssl s_client` against the same host at the same moment validates
  fine, which is presumably a differently-linked/built binary on this
  image. Not something we can patch from the app side (device's system
  OpenSSL, not ours to rebuild).
- **Fix**: `SyncClient`'s `HttpClient` (`Program.cs`,
  `CreateHttpHandler()`) now sets a permanent
  `ServerCertificateCustomValidationCallback`. On
  `SslPolicyErrors.RemoteCertificateChainErrors`, it only overrides the
  failure when *every* chain element's *only* reported status is
  `NotTimeValid`, and independently re-checks each cert's validity window
  using the correctly-parsed managed `NotBefore`/`NotAfter` against
  `DateTimeOffset.UtcNow`. Any other error (untrusted root, revoked,
  tampering, hostname mismatch, or `NotTimeValid` alongside something
  else) still fails closed exactly as before. `FULLVIEW_TLS_DEBUG=1` is
  kept as a permanent opt-in diagnostic flag (logs the override decision)
  for future TLS troubleshooting. Confirmed fixed on-device:
  `sync-once: Succeeded.`
- `tools/device/publish-arm.sh` gained a stray comment during the
  `PublishReadyToRun=false` experiment; reverted cleanly back to `true`
  (confirmed not the cause) with no leftover diff.
- Changes staged, not committed — Dan reviews and commits manually per
  standing instruction.
