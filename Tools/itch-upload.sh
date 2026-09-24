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
die() { printf 'error: %s\n' "$1" >&2; exit 1; }
step() { printf '\n==> %s\n' "$1"; }

source "$SCRIPT_DIR/lib-version.sh"

[ -f "$SCRIPT_DIR/.env.itch" ] && source "$SCRIPT_DIR/.env.itch"

ITCH_PROJECT="${ITCH_PROJECT:-}"
VERSION=""
targets=()
while [ $# -gt 0 ]; do
	case "$1" in
	--project) [ $# -ge 2 ] || die "--project needs a value"; ITCH_PROJECT="$2"; shift 2 ;;
	--version) [ $# -ge 2 ] || die "--version needs a value"; VERSION="$2"; shift 2 ;;
	-*) die "unknown option: $1" ;;
	mac | windows | linux | web) targets+=("$1"); shift ;;
	*) die "unknown target: $1 (expected mac|windows|linux|web)" ;;
	esac
done

[ -n "$ITCH_PROJECT" ] || die "set ITCH_PROJECT (env var, .env.itch, or --project user/game) - find the slug on your itch.io project's dashboard"
[ -n "$VERSION" ] || VERSION="$(latest_version "$PROJECT_DIR/Builds")" ||
	die "no Builds/Version<N> folder to upload - run build-release.sh first"
[[ "$VERSION" =~ ^[0-9]+(\.[0-9]+)?$ ]] || die "invalid version: $VERSION (expected N or N.N)"

PRODUCT="BattlePlan$VERSION"
BUILD_DIR="$PROJECT_DIR/Builds/Version$VERSION"
[ -d "$BUILD_DIR" ] || die "$BUILD_DIR does not exist"

command -v butler >/dev/null 2>&1 || die "butler not found on PATH"
[ -f "$HOME/Library/Application Support/itch/butler_creds" ] || die "not logged in - run 'butler login' first"

push() {
	local channel="$1" path="$2"
	[ -e "$path" ] || { echo "skip $channel: $path not found"; return; }
	step "Pushing $channel <- $path"
	butler push "$path" "$ITCH_PROJECT:$channel" --userversion "$VERSION"
}

[ ${#targets[@]} -eq 0 ] && targets=(mac windows linux web)

step "Uploading version $VERSION from $BUILD_DIR to $ITCH_PROJECT"

for t in "${targets[@]}"; do
	case "$t" in
	mac) push "mac" "$BUILD_DIR/$PRODUCT.dmg" ;;
	windows) push "windows" "$BUILD_DIR/${PRODUCT}Windows.zip" ;;
	linux) push "linux" "$BUILD_DIR/${PRODUCT}Linux.zip" ;;
	web) push "html5" "$BUILD_DIR/${PRODUCT}Web.zip" ;;
	esac
done

step "Done"
butler status "$ITCH_PROJECT"
