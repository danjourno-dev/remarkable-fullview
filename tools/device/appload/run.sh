#!/bin/sh
# AppLoad's launch target. Wraps the real binary so stdout/stderr (including the
# [debug] timing lines Program.cs writes on every tap) land in a known log file
# next to it, regardless of how/where AppLoad itself captures launched-app output.
#
# AppLoad doesn't read /etc/fullview-sync.env or /etc/fullview.env itself (that's systemd's
# job for the fullview-sync.timer path), so the foreground app sources them here too — it's
# the same device-specific config (API base URL, device id, API key, ENABLE_LOGGING) either
# way, and neither file is ever committed to the repo (see docs/device-setup.md).
#
# `set -a` matters: plain `VAR=value` lines picked up by `.` (source) only become
# shell-local variables, not environment variables, so without this `exec` below would
# silently drop them and the app would start with sync disabled and no error — this
# happened for real and looked like a sync bug rather than a shell-scoping one.
DIR="$(cd "$(dirname "$0")" && pwd)"
set -a
[ -f /etc/fullview-sync.env ] && . /etc/fullview-sync.env
[ -f /etc/fullview.env ] && . /etc/fullview.env
set +a
exec "$DIR/Fullview.Device" "$@" >>"$DIR/fullview.log" 2>&1
