#!/usr/bin/env bash
#
# Build BattlePlan1 and push it straight to itch.io.
#
#   ./Tools/release.sh                       # next version, all platforms
#   ./Tools/release.sh mac windows           # next version, subset
#   ./Tools/release.sh --version 1.1 mac     # explicit version
#
# One version is resolved up front and passed to both stages so the build and
# the upload can never disagree about which artifacts are being shipped.
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
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

source "$SCRIPT_DIR/lib-version.sh"

VERSION=""
targets=()
while [ $# -gt 0 ]; do
	case "$1" in
	--version) [ $# -ge 2 ] || die "--version needs a value"; VERSION="$2"; shift 2 ;;
	-*) die "unknown option: $1" ;;
	*) targets+=("$1"); shift ;;
	esac
done

[ -n "$VERSION" ] || VERSION="$(next_version "$PROJECT_DIR/Builds")"
[ ${#targets[@]} -eq 0 ] && targets=(mac windows linux web)

build_targets=()
for t in "${targets[@]}"; do
	case "$t" in
	mac | windows | linux) build_targets+=("$t") ;;
	web) build_targets+=("webgl") ;;
	*) die "unknown target: $t (expected mac|windows|linux|web)" ;;
	esac
done

"$SCRIPT_DIR/build-release.sh" --version "$VERSION" "${build_targets[@]}"
"$SCRIPT_DIR/itch-upload.sh" --version "$VERSION" "${targets[@]}"
