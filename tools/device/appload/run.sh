#!/bin/sh
# AppLoad's launch target. Wraps the real binary so stdout/stderr (including the
# [debug] timing lines Program.cs writes on every tap) land in a known log file
# next to it, regardless of how/where AppLoad itself captures launched-app output.
DIR="$(cd "$(dirname "$0")" && pwd)"
exec "$DIR/Fullview.Device" "$@" >>"$DIR/fullview.log" 2>&1
