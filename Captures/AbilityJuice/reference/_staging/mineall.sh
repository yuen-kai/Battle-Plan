#!/bin/bash
cd "$(dirname "$0")"
for f in vid/*.mp4; do
  [ -f "$f" ] || continue
  tag=$(basename "$f" .mp4)
  python3 mine.py "$f" "$tag" "${1:-16}" 2>&1
done
echo "DONE-MINING"
