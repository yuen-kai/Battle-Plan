#!/usr/bin/env bash
#
# Build BattlePlan1 and push it straight to itch.io.
#
#   ./Tools/release.sh                    # build + upload mac, windows, linux, web
#   ./Tools/release.sh mac windows        # build + upload a subset
#
# Thin wrapper around build-release.sh and itch-upload.sh - see those for
# what each stage actually does and their one-time setup requirements
# (closing the Unity Editor, butler login, ITCH_PROJECT).
#
# "web" here means both the webgl build target and the html5 itch channel -
# build-release.sh/itch-upload.sh call it webgl/web respectively, so it's
# translated below.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

targets=("$@")
[ ${#targets[@]} -eq 0 ] && targets=(mac windows linux web)

build_targets=()
for t in "${targets[@]}"; do
	case "$t" in
	mac | windows | linux) build_targets+=("$t") ;;
	web) build_targets+=("webgl") ;;
	*) die "unknown target: $t (expected mac|windows|linux|web)" ;;
	esac
done

"$SCRIPT_DIR/build-release.sh" "${build_targets[@]}"
"$SCRIPT_DIR/itch-upload.sh" "${targets[@]}"
