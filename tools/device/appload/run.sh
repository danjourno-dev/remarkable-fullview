#!/bin/sh
# AppLoad's launch target. Wraps the real binary so stdout/stderr (including the
# [debug] timing lines Program.cs writes on every tap) land in a known log file
# next to it, regardless of how/where AppLoad itself captures launched-app output.
#
# AppLoad doesn't read /etc/fullview-sync.env itself (that's systemd's job for the
# fullview-sync.timer path), so the foreground app sources it here too — it's the same
# device-specific config (API base URL, device id, API key) either way, and it's never
# committed to the repo (see docs/device-setup.md).
DIR="$(cd "$(dirname "$0")" && pwd)"
[ -f /etc/fullview-sync.env ] && . /etc/fullview-sync.env
exec "$DIR/Fullview.Device" "$@" >>"$DIR/fullview.log" 2>&1
