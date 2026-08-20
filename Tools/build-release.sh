#!/usr/bin/env bash
#
# Automate a Battle Plan release build across platforms.
#
#   ./Tools/build-release.sh              # build mac, windows, linux, webgl
#   ./Tools/build-release.sh mac windows  # build a subset
#
# Runs Unity once per platform in batch mode via Assets/Editor/BuildScript.cs,
# then packages each output to match the Builds/VersionNN convention: macOS
# gets hardened + turned into a .dmg by package-mac-build.sh (app kept, no
# extra mac zip); Windows, Linux, and WebGL get zipped in place. Burst's
# "_BurstDebugInformation_DoNotShip" folder and the raw unzipped build
# directories are stripped afterward - only the .app/.dmg/.zip artifacts ship.
#
# Unity only allows one instance per project path. Close any open Editor
# window on this project before running this script.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
UNITY="/Applications/Unity/Hub/Editor/6000.3.1f1/Unity.app/Contents/MacOS/Unity"
PRODUCT="BattlePlan1"
OUT_DIR="$PROJECT_DIR/Builds/Version1"
LOG_DIR="$PROJECT_DIR/Logs/builds"

die() { printf 'error: %s\n' "$1" >&2; exit 1; }
step() { printf '\n==> %s\n' "$1"; }

[ -x "$UNITY" ] || die "Unity 6000.3.1f1 not found at $UNITY"
if [ -f "$PROJECT_DIR/Temp/UnityLockfile" ] && lsof "$PROJECT_DIR/Temp/UnityLockfile" >/dev/null 2>&1; then
	die "Unity already has this project open - close it first (batch mode needs exclusive access)"
fi

mkdir -p "$OUT_DIR" "$LOG_DIR"

run_build() {
	local target="$1" method="$2" logfile="$LOG_DIR/$3.log"
	step "Building ($method) -> $logfile"
	if ! "$UNITY" -batchmode -quit -nographics \
		-projectPath "$PROJECT_DIR" \
		-buildTarget "$target" \
		-executeMethod "$method" \
		-logFile "$logfile"; then
		tail -n 60 "$logfile" >&2
		die "$method failed - see $logfile"
	fi
	tail -n 20 "$logfile"
}

# Burst emits a "<ProductName>_BurstDebugInformation_DoNotShip" folder next to
# whatever it just built. Strip it before packaging so it never ships.
strip_burst_debug() {
	find "$1" -depth -iname "*_BurstDebugInformation_DoNotShip" -type d -exec rm -rf {} +
}

build_mac() {
	run_build "osx" "BuildScript.BuildMac" "mac"
	strip_burst_debug "$OUT_DIR"
	step "Packaging macOS (.dmg)"
	"$SCRIPT_DIR/package-mac-build.sh" package "$OUT_DIR/$PRODUCT.app"
}

build_windows() {
	run_build "win64" "BuildScript.BuildWindows" "windows"
	strip_burst_debug "$OUT_DIR/${PRODUCT}Windows"
	step "Zipping Windows build"
	( cd "$OUT_DIR" && rm -f "${PRODUCT}Windows.zip" && zip -rq -X "${PRODUCT}Windows.zip" "${PRODUCT}Windows" )
	rm -rf "$OUT_DIR/${PRODUCT}Windows"
}

build_linux() {
	run_build "linux64" "BuildScript.BuildLinux" "linux"
	strip_burst_debug "$OUT_DIR/${PRODUCT}Linux"
	step "Zipping Linux build"
	( cd "$OUT_DIR" && rm -f "${PRODUCT}Linux.zip" && zip -rq -X "${PRODUCT}Linux.zip" "${PRODUCT}Linux" )
	rm -rf "$OUT_DIR/${PRODUCT}Linux"
}

build_webgl() {
	run_build "webgl" "BuildScript.BuildWebGL" "webgl"
	strip_burst_debug "$OUT_DIR/${PRODUCT}Web"
	step "Zipping WebGL build"
	( cd "$OUT_DIR" && rm -f "${PRODUCT}Web.zip" && zip -rq -X "${PRODUCT}Web.zip" "${PRODUCT}Web" )
	rm -rf "$OUT_DIR/${PRODUCT}Web"
}

targets=("$@")
[ ${#targets[@]} -eq 0 ] && targets=(mac windows linux webgl)

for t in "${targets[@]}"; do
	case "$t" in
	mac) build_mac ;;
	windows) build_windows ;;
	linux) build_linux ;;
	webgl) build_webgl ;;
	*) die "unknown target: $t (expected mac|windows|linux|webgl)" ;;
	esac
done

step "Done. Artifacts in $OUT_DIR"
ls -lh "$OUT_DIR"
