#!/bin/bash
cd "$(dirname "$0")"
get() {
  local id="$1" name="$2" sect="$3"
  [ -f "vid/${name}.mp4" ] && { echo "SKIP ${name}"; return; }
  local args=(-f 'bv*[height<=1080][ext=mp4]/bv*[height<=1080]/b[height<=1080]')
  [ -n "$sect" ] && args+=(--download-sections "*${sect}" --force-keyframes-at-cuts)
  yt-dlp "${args[@]}" -o "vid/${name}.%(ext)s" "https://www.youtube.com/watch?v=${id}" >/dev/null 2>&1
  [ -f "vid/${name}.mp4" ] && echo "OK   ${name}" || echo "FAIL ${name} (${id})"
}
get gs73C1rC_ZY cm-nocomm1 "2:00-9:00"
get DAHeMUuOV1M cm-nocomm2 "2:00-9:00"
get WtsuGKjP8bg cm-walkthrough "0:20-2:30"
get kLhA7nxj6ko cm-newboard "0:30-4:30"
get XumtlCHpijU cm-rcdeck "1:00-6:00"
get wlNaVwHx9sc cm-olympics "1:00-6:00"
get yoXQMopl77c cm-betaguide "1:00-5:00"
echo "DONE-DOWNLOADS3"
