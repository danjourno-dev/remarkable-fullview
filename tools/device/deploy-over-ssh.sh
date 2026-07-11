#!/usr/bin/env bash
# Copies the published Fullview.Device binary to the reMarkable and runs it.
# Assumes tools/device/publish-arm.sh has already been run.
#
# Config via env vars (no device details are committed to the repo):
#   DEVICE_HOST  - defaults to 10.11.99.1, the reMarkable's standard USB IP
#   DEVICE_USER  - defaults to root (the device's only SSH user)
#   DEVICE_PATH  - remote install path, defaults to /home/root/fullview-device
#
# The SSH password is whatever you recorded from Settings -> Help ->
# Copyright and licenses on the device; it is never stored by this script.
# Use `ssh-copy-id` once beforehand to avoid retyping it every run.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
BINARY="$REPO_ROOT/artifacts/device/Fullview.Device"

DEVICE_HOST="${DEVICE_HOST:-10.11.99.1}"
DEVICE_USER="${DEVICE_USER:-root}"
DEVICE_PATH="${DEVICE_PATH:-/home/root/fullview-device}"

if [[ ! -f "$BINARY" ]]; then
  echo "Binary not found at $BINARY — run tools/device/publish-arm.sh first." >&2
  exit 1
fi

echo "Copying $BINARY -> $DEVICE_USER@$DEVICE_HOST:$DEVICE_PATH"
scp "$BINARY" "$DEVICE_USER@$DEVICE_HOST:$DEVICE_PATH"

echo "Marking it executable and running it (Ctrl+C to stop; it also exits after 60s)."
ssh "$DEVICE_USER@$DEVICE_HOST" "chmod +x '$DEVICE_PATH' && '$DEVICE_PATH'"
