#!/bin/bash
cd "$(dirname "$0")"
for tag in cm-nocomm1 cm-nocomm2 cm-walkthrough cm-newboard cm-rcdeck cm-olympics cm-betaguide; do
  for i in 1 2 3 4 5 6 7 8 9 10 11 12; do
    [ -f "vid/$tag.mp4" ] && break
    sleep 30
  done
  [ -f "vid/$tag.mp4" ] || { echo "MISSING $tag"; continue; }
  python3 mine.py "vid/$tag.mp4" "$tag" 16 2>&1
done
echo "DONE-MINING3"
