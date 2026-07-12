#!/usr/bin/env bash
# Installs the published Fullview.Device output (binary + native libs + AppLoad manifest +
# icon) as an AppLoad external application, so it's launched from the AppLoad launcher
# instead of by hand over SSH. Assumes tools/device/publish-arm.sh has already been run.
#
# There used to be a fullview-device.service systemd unit here (Restart=on-failure,
# WantedBy=multi-user.target). It's gone: an auto-restarting background service that owns
# the framebuffer is fundamentally incompatible with a launcher-managed app — AppLoad starts
# and stops the process itself, and systemd immediately respawning it after AppLoad's close
# gesture (or after an update) would fight AppLoad for the same qtfb registration. This
# script disables/removes any leftover unit from an older deploy instead.
#
# Stage 5 reintroduces systemd, but only for tools/device/systemd/fullview-sync.{service,timer}
# — a oneshot unit that runs the same binary in FULLVIEW_MODE=sync-once. That mode returns
# before ever opening /dev/fb0, qtfb, or evdev (see Program.cs), so it can't contend with the
# foreground app for the framebuffer the way the old always-on service did; it just drains the
# outbox in the background every 30 minutes (and no-ops without touching the network if
# there's nothing queued).
#
# Config via env vars (no device details are committed to the repo):
#   DEVICE_HOST  - defaults to 10.11.99.1, the reMarkable's standard USB IP
#   DEVICE_USER  - defaults to root (the device's only SSH user)
#   APPLOAD_DIR  - remote AppLoad apps dir, defaults to /home/root/xovi/exthome/appload
#   APP_NAME     - the AppLoad app directory name (and half of its AppLoad ID,
#                  `external::<APP_NAME>`), defaults to fullview
#
# The SSH password is whatever you recorded from Settings -> Help ->
# Copyright and licenses on the device; it is never stored by this script.
# Use `ssh-copy-id` once beforehand to avoid retyping it every run.
#
# fullview-sync.service reads FULLVIEW_API_BASE_URL and FULLVIEW_DEVICE_ID from
# /etc/fullview-sync.env on the device, which this script never writes — create it by hand
# over SSH first (it's device-specific and isn't committed to the repo):
#   FULLVIEW_API_BASE_URL=https://your-api-id.execute-api.your-region.amazonaws.com
#   FULLVIEW_DEVICE_ID=your-device-id
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PUBLISH_DIR="$REPO_ROOT/artifacts/device"
BINARY="$PUBLISH_DIR/Fullview.Device"
MANIFEST_DIR="$SCRIPT_DIR/appload"
SYSTEMD_DIR="$SCRIPT_DIR/systemd"

DEVICE_HOST="${DEVICE_HOST:-10.11.99.1}"
DEVICE_USER="${DEVICE_USER:-root}"
APPLOAD_DIR="${APPLOAD_DIR:-/home/root/xovi/exthome/appload}"
APP_NAME="${APP_NAME:-fullview}"
APP_DIR="$APPLOAD_DIR/$APP_NAME"

if [[ ! -f "$BINARY" ]]; then
  echo "Binary not found at $BINARY — run tools/device/publish-arm.sh first." >&2
  exit 1
fi

if [[ ! -f "$MANIFEST_DIR/external.manifest.json" ]]; then
  echo "Manifest not found at $MANIFEST_DIR/external.manifest.json." >&2
  exit 1
fi

echo "Disabling any leftover fullview-device systemd unit and killing stray processes..."
ssh "$DEVICE_USER@$DEVICE_HOST" '
  systemctl disable --now fullview-device 2>/dev/null || true
  for pid in $(ps | grep "[F]ullview.Device" | awk "{print \$1}"); do kill "$pid"; done
  sleep 1
'

# PublishSingleFile bundles the managed app but leaves native libraries (e.g.
# libe_sqlite3.so) as loose files next to it, so the whole publish directory has to be
# copied, not just the executable. The appload/ directory itself doesn't exist yet on a
# fresh xovi + AppLoad install, hence the mkdir -p.
echo "Installing into $DEVICE_USER@$DEVICE_HOST:$APP_DIR (AppLoad id: external::$APP_NAME)"
ssh "$DEVICE_USER@$DEVICE_HOST" "mkdir -p '$APP_DIR'"
scp "$BINARY" "$PUBLISH_DIR"/*.so "$MANIFEST_DIR/external.manifest.json" "$MANIFEST_DIR/icon.png" \
  "$MANIFEST_DIR/run.sh" "$DEVICE_USER@$DEVICE_HOST:$APP_DIR/"
ssh "$DEVICE_USER@$DEVICE_HOST" "chmod +x '$APP_DIR/Fullview.Device' '$APP_DIR/run.sh'"

echo "Installing fullview-sync.timer (background outbox drain every 30 min)..."
TMP_UNIT_DIR="$(mktemp -d)"
trap 'rm -rf "$TMP_UNIT_DIR"' EXIT
sed "s|__APP_DIR__|$APP_DIR|g" "$SYSTEMD_DIR/fullview-sync.service" > "$TMP_UNIT_DIR/fullview-sync.service"
scp "$TMP_UNIT_DIR/fullview-sync.service" "$SYSTEMD_DIR/fullview-sync.timer" \
  "$DEVICE_USER@$DEVICE_HOST:/etc/systemd/system/"
ssh "$DEVICE_USER@$DEVICE_HOST" '
  systemctl daemon-reload
  systemctl enable --now fullview-sync.timer
'

echo "Installed. Launch \"Fullview\" from the AppLoad launcher on the device."
