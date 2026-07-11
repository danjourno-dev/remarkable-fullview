#!/usr/bin/env bash
# Copies the published Fullview.Device output (binary + native libs) to the
# reMarkable and runs it. Assumes tools/device/publish-arm.sh has already
# been run.
#
# Config via env vars (no device details are committed to the repo):
#   DEVICE_HOST  - defaults to 10.11.99.1, the reMarkable's standard USB IP
#   DEVICE_USER  - defaults to root (the device's only SSH user)
#   DEVICE_DIR   - remote install dir, defaults to /home/root/fullview-device-dir
#
# The SSH password is whatever you recorded from Settings -> Help ->
# Copyright and licenses on the device; it is never stored by this script.
# Use `ssh-copy-id` once beforehand to avoid retyping it every run.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PUBLISH_DIR="$REPO_ROOT/artifacts/device"
BINARY="$PUBLISH_DIR/Fullview.Device"

DEVICE_HOST="${DEVICE_HOST:-10.11.99.1}"
DEVICE_USER="${DEVICE_USER:-root}"
DEVICE_DIR="${DEVICE_DIR:-/home/root/fullview-device-dir}"
DEVICE_PATH="$DEVICE_DIR/Fullview.Device"

if [[ ! -f "$BINARY" ]]; then
  echo "Binary not found at $BINARY — run tools/device/publish-arm.sh first." >&2
  exit 1
fi

# PublishSingleFile bundles the managed app but leaves native libraries
# (e.g. libe_sqlite3.so) as loose files next to it, so the whole publish
# directory has to be copied, not just the executable.
echo "Stopping any previous instance still running on the device..."
ssh "$DEVICE_USER@$DEVICE_HOST" "for pid in \$(ps | grep '[F]ullview.Device' | awk '{print \$1}'); do kill \"\$pid\"; done; sleep 1"

echo "Copying $PUBLISH_DIR/*.so -> $DEVICE_USER@$DEVICE_HOST:$DEVICE_DIR"
ssh "$DEVICE_USER@$DEVICE_HOST" "mkdir -p '$DEVICE_DIR'"
scp "$BINARY" "$PUBLISH_DIR"/*.so "$DEVICE_USER@$DEVICE_HOST:$DEVICE_DIR/"

echo "Marking it executable and running it (Ctrl+C to stop; it also exits after 60s)."
ssh "$DEVICE_USER@$DEVICE_HOST" "chmod +x '$DEVICE_PATH' && '$DEVICE_PATH'"
