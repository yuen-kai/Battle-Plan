#!/usr/bin/env bash
#
# Shared release-version helpers, sourced by build-release.sh, itch-upload.sh
# and release.sh so building and uploading always agree on a version.
#
# Release folders are Builds/Version<major>[.<minor>]. Legacy zero-padded names
# (Version01..Version04) use a superseded scheme and are ignored.

highest_version() {
	local builds="$1" best_major=0 best_minor=0 name major minor
	for d in "$builds"/Version*/; do
		[ -d "$d" ] || continue
		name="${d%/}"
		name="${name##*/Version}"
		[[ "$name" =~ ^[1-9][0-9]*(\.[0-9]+)?$ ]] || continue
		major="${name%%.*}"
		minor="${name#*.}"
		[ "$minor" = "$name" ] && minor=0
		if [ "$major" -gt "$best_major" ] ||
			{ [ "$major" -eq "$best_major" ] && [ "$minor" -gt "$best_minor" ]; }; then
			best_major="$major"
			best_minor="$minor"
		fi
	done
	printf '%s %s' "$best_major" "$best_minor"
}

latest_version() {
	local parts
	parts=($(highest_version "$1"))
	[ "${parts[0]}" -eq 0 ] && return 1
	if [ "${parts[1]}" -eq 0 ]; then
		printf '%s' "${parts[0]}"
	else
		printf '%s.%s' "${parts[0]}" "${parts[1]}"
	fi
}

next_version() {
	local parts
	parts=($(highest_version "$1"))
	[ "${parts[0]}" -eq 0 ] && { printf '1'; return; }
	printf '%s.%s' "${parts[0]}" "$((parts[1] + 1))"
}
