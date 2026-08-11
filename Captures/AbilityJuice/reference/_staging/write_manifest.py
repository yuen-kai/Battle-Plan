#!/usr/bin/env python3
"""Render MANIFEST.md from the assembled selection."""
import json
import os

from PIL import Image

STAGING = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(STAGING)
rows = json.load(open(os.path.join(STAGING, "manifest.json")))

HEAD = {
    "windup": ("windup/", "Anticipation frames: the charge, the gather, the lit ground telegraph "
                          "— everything that happens *before* the effect lands."),
    "impact": ("impact/", "The single most violent frame of each ability: detonation, white-hot "
                          "cores, contact flares."),
    "aftermath": ("aftermath/", "Roughly 0.2-0.5s after impact: expanding smoke, scorch decals, "
                                "lingering glow, falling debris."),
    "general": ("general/", "Full-board gameplay shots showing the overall VFX language in context."),
}

out = []
out.append("# Clash Mini / Clash Royale ability-VFX reference\n")
out.append("Mood board and quality bar for the Battle Plan ability-VFX pass.\n")
out.append("All frames were extracted from gameplay video at source resolution. Ability moments "
           "were located automatically by detecting transient spikes in the fraction of near-white "
           "pixels (a VFX flash), rejecting any spike that straddled an editing cut, then pulling an "
           "aligned triplet at **-0.28s / peak / +0.38s**. That is why `windup/`, `impact/` and "
           "`aftermath/` files that share a name are three moments of the *same* cast and can be "
           "compared directly.\n")
out.append("Every `source URL` is deep-linked to the second the frame was taken from.\n")
out.append("Frames were rejected if they were under 400px on the short edge, blurred pillarbox "
           "padding, menu or loading screens, card art, title cards, commentary captions, or "
           "near-duplicates of a frame already kept.\n")

tally = {}
for r in rows:
    tally[r["cat"]] = tally.get(r["cat"], 0) + 1
out.append("| Category | Frames |\n|---|---|")
for c in ("windup", "impact", "aftermath", "general"):
    out.append(f"| `{c}/` | {tally.get(c, 0)} |")
out.append(f"| **Total** | **{len(rows)}** |\n")

for cat in ("windup", "impact", "aftermath", "general"):
    name, blurb = HEAD[cat]
    out.append(f"\n## `{name}`\n\n{blurb}\n")
    for r in [x for x in rows if x["cat"] == cat]:
        p = os.path.join(ROOT, cat, r["file"])
        if not os.path.exists(p):
            continue
        w, h = Image.open(p).size
        out.append(f"### `{r['file']}`")
        out.append(f"- **Resolution:** {w}x{h}")
        out.append(f"- **Source:** [{r['game']} — {r['title']}]({r['url']})")
        out.append(f"- **Moment:** {r['ability']}")
        out.append(f"- **Why it is a good reference:** {r['desc']}\n")

open(os.path.join(ROOT, "MANIFEST.md"), "w").write("\n".join(out) + "\n")
print("wrote MANIFEST.md", len(rows), "entries")
