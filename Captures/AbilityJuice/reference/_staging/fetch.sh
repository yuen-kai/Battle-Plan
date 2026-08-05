#!/bin/bash
# Download ability-heavy Clash Mini / Clash Royale gameplay for frame extraction.
cd "$(dirname "$0")"
mkdir -p vid

get() {
  local id="$1" name="$2" sect="$3"
  if [ -f "vid/${name}.mp4" ]; then echo "SKIP ${name}"; return; fi
  if [ -n "$sect" ]; then
    yt-dlp -f 'bv*[height<=1080][ext=mp4]/bv*[height<=1080]/b[height<=1080]' \
      --download-sections "*${sect}" --force-keyframes-at-cuts \
      -o "vid/${name}.%(ext)s" "https://www.youtube.com/watch?v=${id}" >/dev/null 2>&1
  else
    yt-dlp -f 'bv*[height<=1080][ext=mp4]/bv*[height<=1080]/b[height<=1080]' \
      -o "vid/${name}.%(ext)s" "https://www.youtube.com/watch?v=${id}" >/dev/null 2>&1
  fi
  if [ -f "vid/${name}.mp4" ]; then echo "OK   ${name}"; else echo "FAIL ${name} (${id})"; fi
}

get biVcytZI5UI cm-clashabilities ""
get rqWq4HunGGg cm-8newabilities ""
get 7APdeXPF-Vg cm-season2 ""
get zs3Z2jWm9QA cm-4thstar "0:15-4:30"
get lyn3HBSEI-8 cm-everyability "1:00-7:00"
get rRm0w06r49Y cm-royalchamp "0:40-5:30"
get Mzm_kLUehr8 cm-gameplay "1:30-7:00"
get i8lTQSjb7JQ cm-monkdeck "0:40-5:00"
get tr55JnOUKVA cm-trophypush "2:00-8:00"
get UQxIBFqV_tU cm-wizard "1:00-5:00"
echo "DONE-DOWNLOADS"
