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
```

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
