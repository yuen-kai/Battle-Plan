#!/usr/bin/env bash
#
# Push a Battle Plan build to itch.io with butler.
#
#   ITCH_PROJECT=your-username/battle-plan ./Tools/itch-upload.sh
#   ./Tools/itch-upload.sh --project your-username/battle-plan windows
#
# One-time setup (run yourself - this opens a browser for your itch.io login):
#   butler login
#
# Set ITCH_PROJECT once you know your project's page (Settings tab on the
# itch.io project dashboard shows the exact "user/game" slug), either via the
# env var above or a .env.itch file (KEY=VALUE, gitignored) next to this
# script. Pushes one channel per platform: mac, windows, linux, web.
#
# Each channel is a persistent slot butler fully replaces on every push (not
# an additional file) - itch.io versions and serves only the latest push per
# channel. This does NOT touch uploads added by hand through the web
# dashboard (e.g. old BattlePlanNN.zip files from before this script
# existed) - those live outside any channel, so delete them yourself on the
# project's edit page if you want them gone.
#
# One-time manual step butler cannot do for you: after the first "web" push,
# open the project's edit page -> Uploads -> the new html5 upload -> check
# "This file will be played in the browser" and save. Itch has no API for
# this flag; it persists for that channel once set.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
PRODUCT="BattlePlan1"
BUILD_DIR="$PROJECT_DIR/Builds/Version1"
VERSION="1.0"

die() { printf 'error: %s\n' "$1" >&2; exit 1; }
step() { printf '\n==> %s\n' "$1"; }

[ -f "$SCRIPT_DIR/.env.itch" ] && source "$SCRIPT_DIR/.env.itch"

ITCH_PROJECT="${ITCH_PROJECT:-}"
if [ "${1:-}" = "--project" ]; then
	ITCH_PROJECT="$2"
	shift 2
fi
[ -n "$ITCH_PROJECT" ] || die "set ITCH_PROJECT (env var, .env.itch, or --project user/game) - find the slug on your itch.io project's dashboard"

command -v butler >/dev/null 2>&1 || die "butler not found on PATH"
[ -f "$HOME/Library/Application Support/itch/butler_creds" ] || die "not logged in - run 'butler login' first"

push() {
	local channel="$1" path="$2"
	[ -e "$path" ] || { echo "skip $channel: $path not found"; return; }
	step "Pushing $channel <- $path"
	butler push "$path" "$ITCH_PROJECT:$channel" --userversion "$VERSION"
}

targets=("$@")
[ ${#targets[@]} -eq 0 ] && targets=(mac windows linux web)

for t in "${targets[@]}"; do
	case "$t" in
	mac) push "mac" "$BUILD_DIR/$PRODUCT.dmg" ;;
	windows) push "windows" "$BUILD_DIR/${PRODUCT}Windows.zip" ;;
	linux) push "linux" "$BUILD_DIR/${PRODUCT}Linux.zip" ;;
	web) push "html5" "$BUILD_DIR/${PRODUCT}Web.zip" ;;
	*) die "unknown target: $t (expected mac|windows|linux|web)" ;;
	esac
done

step "Done"
butler status "$ITCH_PROJECT"
