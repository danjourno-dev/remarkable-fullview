#!/usr/bin/env bash
# Publishes Fullview.Device as a self-contained linux-arm (armhf) binary
# for the reMarkable 1. Output goes to artifacts/device/ (gitignored).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT="$REPO_ROOT/src/Fullview.Device/Fullview.Device.csproj"
OUTPUT_DIR="$REPO_ROOT/artifacts/device"

echo "Publishing Fullview.Device (linux-arm, self-contained) -> $OUTPUT_DIR"

dotnet publish "$PROJECT" \
  -c Release \
  -r linux-arm \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=false \
  -o "$OUTPUT_DIR"

echo "Published: $OUTPUT_DIR/Fullview.Device"
