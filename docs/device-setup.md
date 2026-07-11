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

```bash
tools/device/publish-arm.sh          # self-contained linux-arm build -> artifacts/device/
DEVICE_HOST=<device-ip> tools/device/deploy-over-ssh.sh   # scp + run once, foreground
```

`deploy-over-ssh.sh` env vars (all optional):

| Var | Default | Meaning |
|---|---|---|
| `DEVICE_HOST` | `10.11.99.1` | reMarkable's standard USB IP |
| `DEVICE_USER` | `root` | the device's only SSH user |
| `DEVICE_DIR` | `/home/root/fullview-device-dir` | remote install directory |

`PublishSingleFile` bundles the managed app into one executable but leaves
native libraries (e.g. `libe_sqlite3.so`, used by the SQLite store) as loose
files next to it, so the whole publish output directory is copied to the
device, not just the executable.

## Runtime configuration

The app reads three optional environment variables at startup:

| Var | Default | Meaning |
|---|---|---|
| `FULLVIEW_DB_PATH` | `<binary's directory>/fullview.db` | SQLite store location |
| `FULLVIEW_TOUCH_DEVICE` | `/dev/input/event2` | evdev node for the capacitive touchscreen |
| `FULLVIEW_BUTTON_DEVICE` | `/dev/input/event1` | evdev node for the physical buttons — the right-hand button switches Personal/Work mode |

The touch default was confirmed on hardware via `cat /proc/bus/input/devices`:
event0 is the Wacom pen digitizer, event1 is `gpio-keys` (the physical side
buttons), and event2 is `cyttsp5_mt`, the capacitive touch controller. The
button device's key code (`KEY_RIGHT`, the standard linux/input-event-codes.h
value gpio-keys is expected to report for the right-hand button) is not yet
confirmed on real hardware — if the button doesn't switch mode, check the
actual code with `evtest /dev/input/event1` (or `cat`ing the device while
pressing it) and update `EvCodes.KEY_RIGHT` in
`src/Fullview.Device/Input/RawInputEvent.cs`. If your unit's touch or button
node differs, run the same `/proc/bus/input/devices` command on the device to
find the right `eventN` and set `FULLVIEW_TOUCH_DEVICE`/`FULLVIEW_BUTTON_DEVICE`
accordingly — either as a one-off env var, or by adding an `Environment=` line
to the systemd unit below.

## Running as a background service

Once `deploy-over-ssh.sh` has confirmed the binary runs correctly in the
foreground, install it as a systemd service so it starts on boot and
restarts if it crashes:

```bash
scp tools/device/fullview-device.service root@<device-ip>:/etc/systemd/system/
ssh root@<device-ip> "systemctl daemon-reload && systemctl enable --now fullview-device"
```

Check it's running and see its logs:

```bash
ssh root@<device-ip> "systemctl status fullview-device"
ssh root@<device-ip> "journalctl -u fullview-device -f"
```
