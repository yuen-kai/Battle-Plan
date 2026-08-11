#!/bin/bash
cd "$(dirname "$0")"
mkdir -p vid

get() {
  local id="$1" name="$2" sect="$3"
  if [ -f "vid/${name}.mp4" ]; then echo "SKIP ${name}"; return; fi
  local args=(-f 'bv*[height<=1080][ext=mp4]/bv*[height<=1080]/b[height<=1080]')
  [ -n "$sect" ] && args+=(--download-sections "*${sect}" --force-keyframes-at-cuts)
  yt-dlp "${args[@]}" -o "vid/${name}.%(ext)s" \
    "https://www.youtube.com/watch?v=${id}" >/dev/null 2>&1
  if [ -f "vid/${name}.mp4" ]; then echo "OK   ${name}"; else echo "FAIL ${name} (${id})"; fi
}

# Clash Mini
get RQ0zd5XlgJM cm-trailer ""
get 4HvEeQrRYQ0 cm-betacinematic ""
get 6samtOj3yPw cm-season3 ""
get 352NvDxGUDI cm-firstlook "0:30-3:30"
get wsfdFUyxQns cm-clashmas ""
# Clash Royale (same art dept, same VFX language) - spell impacts
get qxWXGfbeY-g cr-allspells "0:10-2:10"
get EVD4Fhlvzyg cr-spellsbuildings "0:20-4:00"
get 720rhIewkoA cr-lightningrod ""
get _kqIYPS9m2E cr-spellguide "1:00-6:00"
get zwo2HTMvRW8 cr-animations "0:40-5:00"
echo "DONE-DOWNLOADS2"
