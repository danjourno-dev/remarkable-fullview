# Device setup

How to build, deploy, and run `Fullview.Device` on a reMarkable 1. Written
for anyone forking this repo — nothing here is specific to any one device.

## Prerequisites

- A reMarkable 1 with SSH access enabled (Settings → Help → Copyright and
  licenses has the SSH password) and automatic updates turned off, so a
  firmware update doesn't wipe your changes underneath you.
- The device's OS version determines your patching tool: if it's within
  [Toltec](https://toltec-dev.org/)'s supported ceiling, Toltec works; if
  it's newer, use [Vellum](https://github.com/vellum-dev/vellum-cli)
  instead (`vellum check-os` tells you which packages it can offer for your
  exact OS build). Either way you need a launcher installed to run apps
  from the device's home screen — AppLoad is the actively maintained one
  and is Vellum-compatible; Toltec's `remux` is not.
- .NET 8 SDK on your dev machine (`dotnet --version`).

## Build and deploy

The app is launched from the AppLoad launcher, not by hand over SSH — AppLoad
starts it with `qtfb: true`, which gives it a shared drawing surface instead
of raw `/dev/fb0` access, so it no longer fights `xochitl` for the
framebuffer.

```bash
tools/device/publish-arm.sh          # self-contained linux-arm build -> artifacts/device/
DEVICE_HOST=<device-ip> tools/device/deploy-over-ssh.sh   # installs into AppLoad
```

This installs the app as an AppLoad external application at
`/home/root/xovi/exthome/appload/fullview/` (AppLoad id `external::fullview`).
Launch "Fullview" from the AppLoad launcher on the device's home screen; drag
from the center-top of the screen to the center to close it.

`deploy-over-ssh.sh` env vars (all optional):

| Var | Default | Meaning |
|---|---|---|
| `DEVICE_HOST` | `10.11.99.1` | reMarkable's standard USB IP |
| `DEVICE_USER` | `root` | the device's only SSH user |
| `APPLOAD_DIR` | `/home/root/xovi/exthome/appload` | remote AppLoad apps directory |
| `APP_NAME` | `fullview` | the AppLoad app directory name / id suffix |

`PublishSingleFile` bundles the managed app into one executable but leaves
native libraries (e.g. `libe_sqlite3.so`, used by the SQLite store) as loose
files next to it, so the whole publish output directory is copied to the
device, not just the executable.

The manifest (`tools/device/appload/external.manifest.json`) sets
`qtfb: true`, so AppLoad passes a framebuffer key in the `QTFB_KEY`
environment variable and the app talks to AppLoad's qtfb socket
(`/tmp/qtfb.sock`) instead of opening `/dev/fb0` directly — see
`QtfbScreen`/`QtfbInputSource` in `src/Fullview.Device/`. Running the binary
by hand over SSH without `QTFB_KEY` set still works for local debugging: it
falls back to `FramebufferDevice` (direct `/dev/fb0` + evdev), the original
hand-launch path.

The manifest's `application` points at `run.sh`, a thin wrapper that `exec`s
`Fullview.Device` with stdout/stderr appended to `fullview.log` in the same
app directory — this guarantees the app's console output (including the
per-tap `[debug] Timing: ...` breakdown logged in `Program.cs`) lands
somewhere you can read over SSH, regardless of how AppLoad itself handles a
launched app's own stdout. `tail -f
/home/root/xovi/exthome/appload/fullview/fullview.log` while using the app on
the device is the fastest way to see what's actually slow.

The publish script passes `-p:PublishReadyToRun=true`, which ahead-of-time
compiles the app and its dependencies (ImageSharp in particular) to native ARM
code at publish time instead of JIT-compiling them on first use. This is
meant to cut down cold-start time on the rM1's slow CPU; it doesn't require
trimming (`PublishTrimmed` stays `false` — the SQLite store uses
reflection-based JSON serialization that trimming can silently break).

## Runtime configuration

The app reads these optional environment variables at startup:

| Var | Default | Meaning |
|---|---|---|
| `FULLVIEW_DB_PATH` | `<binary's directory>/fullview.db` | SQLite store location |
| `FULLVIEW_TOUCH_DEVICE` | `/dev/input/event2` | evdev node for the capacitive touchscreen |
| `FULLVIEW_BUTTON_DEVICE` | `/dev/input/event1` | evdev node for the physical buttons — the right-hand button switches Personal/Work mode |
| `FULLVIEW_DEVICE_ID` | `device` | this device's id in sync payloads (`UpdatedBy`, `/sync` request) |
| `FULLVIEW_API_BASE_URL` | unset | base URL of the deployed `/sync` API (e.g. `https://<id>.execute-api.<region>.amazonaws.com`); sync is disabled entirely if unset |
| `FULLVIEW_API_KEY` | unset | the shared API key checked by API Gateway's authorizer (see "API authentication" below); every `/sync` call is rejected with 401 if unset or wrong, which the app treats like any other sync failure |
| `FULLVIEW_MODE` | `app` | `app` (default, foreground UI) or `sync-once` (headless outbox drain, see below) |

The touch default was confirmed on hardware via `cat /proc/bus/input/devices`:
event0 is the Wacom pen digitizer, event1 is `gpio-keys` (the physical side
buttons), and event2 is `cyttsp5_mt`, the capacitive touch controller. The
button device's key code (`KEY_RIGHT`, the standard linux/input-event-codes.h
value gpio-keys is expected to report for the right-hand button) is not yet
confirmed on real hardware — if the button doesn't switch mode, check the
actual code with `evtest /dev/input/event1` (or `cat`ing the device while
pressing it) and update `EvCodes.KEY_RIGHT` in
`src/Fullview.Device/Input/RawInputEvent.cs`. These two only apply to the
direct-SSH fallback path (no `QTFB_KEY`); under AppLoad, touch/button input
arrives over the qtfb socket instead — see `QtfbInputSource`. If your unit's
touch or button node differs, run the same `/proc/bus/input/devices` command
on the device to find the right `eventN` and set
`FULLVIEW_TOUCH_DEVICE`/`FULLVIEW_BUTTON_DEVICE` accordingly, either as a
one-off env var or via the manifest's `environment` map (see
`tools/device/appload/external.manifest.json`).

There is deliberately no systemd unit or other auto-restarting background
service running the *foreground app*: AppLoad owns starting and stopping it
(from the launcher, and via the drag-to-close gesture), and an independently
restarting service would fight AppLoad for the same qtfb registration.

## Background sync timer (Stage 5)

`tools/device/deploy-over-ssh.sh` installs one systemd unit —
`fullview-sync.timer` — that periodically runs the same binary in
`FULLVIEW_MODE=sync-once`. That mode returns before ever opening `/dev/fb0`,
qtfb, or evdev, so it can't contend with the AppLoad-launched foreground app
for the framebuffer; it just drains the local outbox to `/sync` in the
background, roughly every 30 minutes, and does nothing at all (no network
call) if there's nothing queued to push.

Before deploying, create `/etc/fullview-sync.env` on the device by hand over
SSH (it's device-specific, so the deploy script never writes it and it's
never committed to the repo):

```
FULLVIEW_API_BASE_URL=https://<your-api-id>.execute-api.<your-region>.amazonaws.com
FULLVIEW_DEVICE_ID=<your-device-id>
FULLVIEW_API_KEY=<your-api-key, see "API authentication" below>
```

`run.sh` (the AppLoad launch target) sources this same file before starting
the foreground app, so one file covers both the foreground app's startup/
manual sync and the background timer's `sync-once` runs.

After a deploy, verify the timer is scheduled and check its most recent run:

```
ssh root@10.11.99.1 systemctl status fullview-sync.timer
ssh root@10.11.99.1 systemctl status fullview-sync.service
ssh root@10.11.99.1 journalctl -u fullview-sync.service -n 20
```

To force an immediate drain without waiting for the next scheduled firing
(useful when testing Checkpoint 5.1's kill-WiFi scenario):

```
ssh root@10.11.99.1 systemctl start fullview-sync.service
```

## API authentication

`/sync` is protected by a REQUEST authorizer on the HTTP API: every call must
carry an `x-api-key` header matching a single shared secret held as a
SecureString in SSM Parameter Store. This is deliberately the simplest thing
that works for a single-user app (see docs/plans/implementation.md) — there's
no login flow, just one key that either matches or doesn't. `/health` stays
unauthenticated (no sensitive data, useful as a plain liveness probe).

CloudFormation can't create SecureString parameters, so the CDK stack
(`FullviewStack`) never creates this parameter — only Dan (or a forker)
creates it, once, by hand:

```bash
aws ssm put-parameter \
  --name /fullview-api-key \
  --type SecureString \
  --value "$(openssl rand -hex 32)"
```

Then read it back once to put the same value in `/etc/fullview-sync.env` on
the device (see above) as `FULLVIEW_API_KEY`:

```bash
aws ssm get-parameter --name /fullview-api-key --with-decryption --query Parameter.Value --output text
```

A forker deploying their own stack under a different `ResourcePrefix` uses
`/<their-prefix>api-key` instead — the authorizer Lambda's
`FULLVIEW_API_KEY_PARAM` environment variable (set by the CDK stack) always
points at the right name for that deployment.

**Fullview.Web (Stage 6)** bakes the same key into its browser bundle at
build time (`VITE_API_KEY`) — see Fullview.Web/README.md for why that's an
accepted v1 tradeoff (single user, Cognito is v2). Locally, put it in
`src/Fullview.Web/.env.local` (gitignored). In CI, `cd-web.yml` reads it from
the **`FULLVIEW_API_KEY` GitHub Actions secret** (Settings → Secrets and
variables → Actions → New repository secret) — set it to the same value as
the SSM parameter above.

## Google Calendar sync (Stage 6.5)

`CalendarPullFunction` mirrors one or more Google calendars into the board's
agenda every 15 minutes (EventBridge schedule). It is context-agnostic — a
calendar's Work/Personal tagging comes entirely from the config parameter
below, never from event content. Like the API key above, none of this ever
lives in the repo or GitHub — three parameters, all created by hand.

### 1. Google Cloud OAuth client (checkpoint 6.5.1)

1. Google Cloud Console → create a project (e.g. `remarkable-fullview-personal`).
2. **APIs & Services → Library** → enable the **Google Calendar API**.
3. **APIs & Services → OAuth consent screen** → User type **External**.
   Publishing status stays **Testing** with yourself as the sole test user —
   this avoids Google's app-verification process entirely, and a
   Testing-mode refresh token works fine for single-user use (it just needs
   re-consenting roughly every 7 days *unless* you're listed as a test user,
   in which case it doesn't expire).
4. **Credentials → Create credentials → OAuth client ID** → application type
   **Desktop app** (no redirect URI to host).
5. Put the client id and secret into SSM as a single JSON SecureString:

   ```bash
   aws ssm put-parameter \
     --name /fullview-google-oauth-client \
     --type SecureString \
     --value '{"clientId":"<client id>.apps.googleusercontent.com","clientSecret":"<client secret>"}'
   ```

### 2. One-time consent + refresh token (checkpoint 6.5.2)

Run the console app in `tools/google-auth/` once, from a machine with a
browser (this is a local dev-time step, not part of the deploy pipeline):

```bash
dotnet run --project tools/google-auth
```

It prompts for the client id/secret from step 1, opens your browser to
consent (scope is **`calendar.readonly`** — read-only, so even a compromised
token can't alter your calendars), then prints a refresh token. Put it
straight into SSM — do not save it to a file:

```bash
aws ssm put-parameter \
  --name /fullview-google-refresh-token \
  --type SecureString \
  --value "<refresh token printed above>"
```

One consent covers every calendar on the account, which is why the
two-calendar (Personal + Work) model below costs nothing extra.

### 3. Calendar → context mapping

Not a secret — a plain String parameter, and the only place the Work/Personal
mapping lives. Adding, removing, or re-tagging a calendar is a config change
here, never a code change:

```bash
aws ssm put-parameter \
  --name /fullview-google-calendars \
  --type String \
  --value '[{"id":"you@gmail.com","context":"Personal"},{"id":"<work-mirror calendar id>","context":"Work"}]'
```

Calendar ids are visible in Google Calendar → Settings → *(calendar name)* →
"Integrate calendar" → **Calendar ID**.

### 4. Live verification (checkpoint 6.5.3)

Add a test event to your primary Google calendar and confirm it appears on
the board in **Personal** mode within ~15 min. Then confirm a real event on
the Work-mapped calendar appears in **Work** mode. Finally, confirm both show
up in the Now/Next strip regardless of which mode the board is currently in
(the strip always renders cross-context — see B3). Pulled events render with
a subtle marker and no tap-to-edit affordance, since remarkable-fullview
never writes back to Google.

**Forking this repo?** Bring your own Google Cloud project, your own OAuth
client, and your own refresh token in your own SSM — none of the above is
shared or reusable across accounts. The Outlook-mirroring flow that populates
a `Work (mirror)` calendar (documented separately) is an optional recipe, not
a dependency: the puller neither knows nor cares how a calendar got
populated.
