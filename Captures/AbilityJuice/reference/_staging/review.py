#!/usr/bin/env python3
"""Contact sheet of ranked candidates for one category, in rank order."""
import json
import os
import sys

import sheet

STAGING = os.path.dirname(os.path.abspath(__file__))
rows = json.load(open(os.path.join(STAGING, "ranked.json")))
cat, start, count = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
sel = [r for r in rows if r["cat"] == cat][start:start + count]
paths = [r["path"] for r in sel]
out = os.path.join(STAGING, f"rev_{cat}_{start}.jpg")
sheet.build(paths, out)
for i, r in enumerate(sel, start):
    t = f"{r['t']:.1f}s" if r["t"] is not None else "-"
    print(f"{i:3d} {os.path.basename(r['path']):46s} {r['w']}x{r['h']} t={t} hot={r['hot']:.3f}")
print("->", out, f"({len(sel)} of {sum(1 for r in rows if r['cat']==cat)})")
