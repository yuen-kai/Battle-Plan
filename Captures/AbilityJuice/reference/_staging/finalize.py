#!/usr/bin/env python3
"""Rank surviving candidates and stage the selection metadata.

Peak detection is deterministic given the cached profiles, so source timestamps
can be recovered here without re-decoding any video.
"""
import json
import os
import sys

import numpy as np
from PIL import Image

import cull
import mine

STAGING = os.path.dirname(os.path.abspath(__file__))
CAND = os.path.join(STAGING, "cand")
SOURCES = json.load(open(os.path.join(STAGING, "sources.json")))


# Peak numbering depends on the cap used at mining time, so it has to be replayed
# with the same value per source.
CAPS = {"cm-everyability": 30, "cm-8newabilities": 30,
        "cm-clashabilities": 25, "cm-season2": 25,
        "cm-trailer": 14, "cm-betacinematic": 14, "cm-season3": 14,
        "cm-firstlook": 14, "cm-clashmas": 14, "cr-allspells": 14,
        "cr-spellsbuildings": 14, "cr-lightningrod": 14, "cr-animations": 14}


def peak_times():
    """filename-stem -> (source seconds, peak strength) for every mined triplet."""
    out = {}
    for tag, meta in SOURCES.items():
        vid = os.path.join(STAGING, "vid", tag + ".mp4")
        if not os.path.exists(vid):
            continue
        pj = os.path.join(STAGING, "prof", tag + ".mp4.json.npy")
        ph = os.path.join(STAGING, "prof", tag + ".mp4.hi.npy")
        if not (os.path.exists(pj) and os.path.exists(ph)):
            continue
        arr, hi = np.load(pj), np.load(ph)
        peaks = mine.find_peaks(arr, hi, CAPS.get(tag, 16))
        for k, (ti, d, s) in enumerate(peaks, 1):
            for cat, off in (("windup", mine.WINDUP_OFF),
                             ("impact", 0.0),
                             ("aftermath", mine.AFTERMATH_OFF)):
                out[f"{tag}-{k:02d}-{cat}"] = (ti + off + meta["offset"], d, tag)
    return out


def main():
    times = peak_times()
    rows = []
    for cat in ("windup", "impact", "aftermath", "general"):
        d = os.path.join(CAND, cat)
        if not os.path.isdir(d):
            continue
        for f in sorted(os.listdir(d)):
            if not f.lower().endswith(".jpg"):
                continue
            p = os.path.join(d, f)
            stem = f[:-4]
            im = Image.open(p)
            g = np.asarray(im.convert("L"), dtype=np.float32)
            a = np.asarray(im.convert("RGB"), dtype=np.float32)
            mx, mn = a.max(axis=2), a.min(axis=2)
            t, strength, tag = times.get(stem, (None, 0.0, stem.split("-g")[0]))
            rows.append(dict(
                path=p, cat=cat, stem=stem, tag=tag,
                w=im.width, h=im.height,
                t=t, strength=float(strength),
                hot=float((g > 225).mean()),
                sat=float(((mx - mn) / (mx + 1e-6)).mean()),
            ))
    # strongest VFX first, but round-robin across source videos for variety
    rows.sort(key=lambda r: -(r["strength"] + 40 * r["hot"]))
    json.dump(rows, open(os.path.join(STAGING, "ranked.json"), "w"), indent=1)
    per_cat = {}
    for r in rows:
        per_cat[r["cat"]] = per_cat.get(r["cat"], 0) + 1
    print("surviving candidates:", per_cat, "total", len(rows))


if __name__ == "__main__":
    main()
