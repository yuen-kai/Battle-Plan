#!/bin/bash
cd "$(dirname "$0")"
for tag in cm-trailer cm-betacinematic cm-season3 cm-firstlook cm-clashmas cr-allspells cr-spellsbuildings cr-lightningrod cr-animations; do
  [ -f "vid/$tag.mp4" ] || continue
  python3 mine.py "vid/$tag.mp4" "$tag" 14 2>&1
done
echo "DONE-MINING2"
