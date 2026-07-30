#!/usr/bin/env bash
#
# Prepare a Unity macOS player for distribution to other Macs.
#
#   ./Tools/package-mac-build.sh package Builds/Version03/BattlePlan.app
#   ./Tools/package-mac-build.sh repair  /Applications/BattlePlan.app
#
# package - harden the bundle and build a drag-to-Applications .dmg. Add --zip
#           for upload targets that require an archive instead.
# repair  - fix an already-downloaded bundle in place on the receiving Mac

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Must match the canvas in dmg-background.swift, or the artwork will not line up
# with the icons Finder draws on top of it.
readonly WINDOW_W=700
readonly WINDOW_H=600
readonly ICON_SIZE=128
readonly APP_ICON_X=175
readonly APP_ICON_Y=205
readonly APPS_ICON_X=525
readonly APPS_ICON_Y=205
readonly LINK_ICON_X=175
readonly LINK_ICON_Y=425

# .webloc rejects non-web schemes, but .inetloc still resolves this one, and it
# keeps working while the file carries a download quarantine flag.
readonly SETTINGS_LINK_NAME="Open Settings.inetloc"
readonly SETTINGS_URL="x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension"

TMPROOT=""
cleanup() {
	if [ -n "$TMPROOT" ]; then rm -rf "$TMPROOT"; fi
}
trap cleanup EXIT

die() { printf 'error: %s\n' "$1" >&2; exit 1; }
warn() { printf 'warning: %s\n' "$1" >&2; }
step() { printf '\n==> %s\n' "$1"; }

restore_exec_bits() {
	local app="$1" count=0 f
	while IFS= read -r f; do
		if [[ "$(file -b "$f")" == Mach-O* ]]; then
			chmod 755 "$f"
			printf '    +x %s\n' "${f#"$app"/}"
			count=$((count + 1))
		fi
	done < <(find "$app" -type f)
	find "$app" -type d -exec chmod 755 {} +
	printf '    %d executables restored\n' "$count"
	[ "$count" -gt 0 ] || die "no Mach-O binaries found in $app - is it a real .app bundle?"
}

# Apple silicon refuses to launch a bundle whose signature does not match its
# contents, and chmod invalidates the seal, so re-sign after touching perms.
resign_adhoc() {
	codesign --force --deep --sign - "$1"
	codesign --verify --deep --strict "$1" || die "signature verification failed"
}

harden() {
	local app="$1"
	step "Restoring executable permissions"
	restore_exec_bits "$app"
	step "Stripping quarantine"
	xattr -cr "$app"
	step "Re-signing (ad-hoc)"
	resign_adhoc "$app"
}

# Combines a 1x and 2x render into one multi-representation TIFF so the backdrop
# stays sharp on Retina displays.
render_background() {
	local dest="$1" work
	command -v swift >/dev/null 2>&1 || return 1
	[ -f "$SCRIPT_DIR/dmg-background.swift" ] || return 1
	work="$TMPROOT/bg"
	mkdir -p "$work"
	swift "$SCRIPT_DIR/dmg-background.swift" "$work/bg.png" 1 >/dev/null 2>&1 || return 1
	swift "$SCRIPT_DIR/dmg-background.swift" "$work/bg@2x.png" 2 >/dev/null 2>&1 || return 1
	tiffutil -cathidpicheck "$work/bg.png" "$work/bg@2x.png" -out "$dest" >/dev/null 2>&1 || return 1
}

# Best-effort: styling drives Finder over AppleScript, which needs Automation
# permission. A refusal costs the artwork, not the disk image.
style_window() {
	local volname="$1" appname="$2" background="$3" backdrop=""
	if [ "$background" = "yes" ]; then
		backdrop='set background picture of opts to file ".background:background.tiff"'
	fi
	osascript <<-APPLESCRIPT
		tell application "Finder"
			tell disk "$volname"
				open
				set current view of container window to icon view
				set toolbar visible of container window to false
				set statusbar visible of container window to false
				set the bounds of container window to {200, 120, $((200 + WINDOW_W)), $((120 + WINDOW_H))}
				set opts to the icon view options of container window
				set arrangement of opts to not arranged
				set icon size of opts to $ICON_SIZE
				set text size of opts to 12
				$backdrop
				set position of item "$appname" of container window to {$APP_ICON_X, $APP_ICON_Y}
				set position of item "Applications" of container window to {$APPS_ICON_X, $APPS_ICON_Y}
				set position of item "$SETTINGS_LINK_NAME" of container window to {$LINK_ICON_X, $LINK_ICON_Y}
				update without registering applications
				delay 1
				close
			end tell
		end tell
	APPLESCRIPT
}

# Drops the recipient straight onto the pane that holds the "Open Anyway"
# button, which is otherwise four levels deep in System Settings.
write_settings_link() {
	cat >"$1" <<-PLIST
		<?xml version="1.0" encoding="UTF-8"?>
		<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
		<plist version="1.0">
		<dict>
		    <key>URL</key>
		    <string>$SETTINGS_URL</string>
		</dict>
		</plist>
	PLIST

	if command -v swift >/dev/null 2>&1 && [ -f "$SCRIPT_DIR/dmg-link-icon.swift" ]; then
		swift "$SCRIPT_DIR/dmg-link-icon.swift" "$1" >/dev/null 2>&1 ||
			warn "could not theme the shortcut icon"
	fi
}

build_dmg() {
	local app="$1" dmg="$2" volname="$3"
	local appname stage rw mount device sizemb background=no

	appname="$(basename "$app")"
	stage="$TMPROOT/stage"
	rw="$TMPROOT/rw.dmg"
	mount="/Volumes/$volname"

	mkdir -p "$stage/.background"
	ditto "$app" "$stage/$appname"
	ln -s /Applications "$stage/Applications"
	write_settings_link "$stage/$SETTINGS_LINK_NAME"

	if render_background "$stage/.background/background.tiff"; then
		background=yes
	else
		warn "could not render the backdrop; shipping a plain window"
	fi

	if [ -d "$mount" ]; then
		hdiutil detach "$mount" -force -quiet 2>/dev/null || true
	fi

	sizemb=$(( $(du -sm "$stage" | cut -f1) + 80 ))
	hdiutil create -srcfolder "$stage" -volname "$volname" -fs HFS+ \
		-format UDRW -size "${sizemb}m" -ov -quiet "$rw"

	local attached
	attached="$(hdiutil attach "$rw" -readwrite -noverify -noautoopen)"
	device="$(awk '/^\/dev\// { print $1; exit }' <<<"$attached")"
	[ -n "$device" ] || die "could not mount the working image"

	if ! style_window "$volname" "$appname" "$background"; then
		warn "Finder declined to style the window; the disk image is still valid"
	fi

	# Hiding the extension only now keeps the name AppleScript matched on intact.
	SetFile -a E "$mount/$SETTINGS_LINK_NAME" 2>/dev/null || true

	# Finder consumes .VolumeIcon.icns when it opens the volume, so the mounted
	# image only keeps the icon if it is written after the window is styled.
	if [ -f "$app/Contents/Resources/PlayerIcon.icns" ]; then
		cp "$app/Contents/Resources/PlayerIcon.icns" "$mount/.VolumeIcon.icns"
		SetFile -a C "$mount" 2>/dev/null || true
	fi

	sync
	hdiutil detach "$device" -quiet || hdiutil detach "$device" -force -quiet

	rm -f "$dmg"
	hdiutil convert "$rw" -format UDZO -imagekey zlib-level=9 -o "$dmg" -quiet
}

cmd_repair() {
	local app="${1:-}"
	[ -d "$app" ] || die "not a bundle directory: ${app:-<missing argument>}"
	harden "$app"
	printf '\nRepaired %s - it should now launch.\n' "$app"
}

cmd_package() {
	local app="" want_zip=no
	while [ $# -gt 0 ]; do
		case "$1" in
			--zip) want_zip=yes ;;
			-*) die "unknown option: $1" ;;
			*) app="$1" ;;
		esac
		shift
	done
	[ -d "$app" ] || die "not a bundle directory: ${app:-<missing argument>}"

	local outdir name appname dmg zip
	outdir="$(cd "$(dirname "$app")" && pwd)"
	appname="$(basename "$app")"
	name="$(basename "$app" .app)"
	dmg="$outdir/$name.dmg"
	zip="$outdir/$name-mac.zip"

	TMPROOT="$(mktemp -d)"

	harden "$app"

	# A disk image is the only container that reliably survives browsers and
	# cloud storage with permissions and bundle structure intact.
	step "Building $dmg"
	build_dmg "$app" "$dmg" "$name"

	# Only for upload targets that insist on an archive. ditto records real Unix
	# modes, but a zip keeps them only while nothing in the delivery chain
	# re-compresses it, and it carries none of the disk image's instructions.
	if [ "$want_zip" = yes ]; then
		step "Building $zip"
		rm -f "$zip"
		ditto -c -k --sequesterRsrc --keepParent "$app" "$zip"
	fi

	step "Done"
	if [ "$want_zip" = yes ]; then
		ls -lh "$dmg" "$zip"
	else
		ls -lh "$dmg"
	fi
	cat <<-EOF

	Ship the .dmg - it opens to a drag-into-Applications window with the
	first-launch steps on the backdrop and an "Open Settings" shortcut that
	jumps straight to Privacy & Security.

	This build is ad-hoc signed but not notarised, so the first launch on
	another Mac needs a one-time approval. Control-clicking Open no longer
	works for this; macOS 15 and later require the System Settings route:

	  1. Drag $appname into Applications, then launch it from there.
	  2. Click Done on the "cannot be verified" warning.
	  3. Open the shortcut in the disk image, scroll to Security,
	     click "Open Anyway", then authenticate.

	Terminal equivalent for the recipient:
	  xattr -dr com.apple.quarantine "/Applications/$appname"
	EOF
}

main() {
	[ "$(uname)" = "Darwin" ] || die "macOS only"
	case "${1:-}" in
		package) shift; cmd_package "$@" ;;
		repair)  shift; cmd_repair "$@" ;;
		*) die "usage: $0 package [--zip] <path/to/App.app> | repair <path/to/App.app>" ;;
	esac
}

main "$@"
