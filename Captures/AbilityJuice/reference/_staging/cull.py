#!/usr/bin/env python3
"""Clean candidate frames: crop blurred pillarbox, drop junk, drop near-duplicates.

Videos mix full-screen and portrait-pillarboxed segments, so cropping has to be
decided per frame rather than per video.
"""
import os
import sys

import numpy as np
from PIL import Image

MIN_LONG_EDGE = 400


def sharp_span(g):
    """Horizontal span of the crisp gameplay area.

    Portrait phone footage is padded with a blown-up blurred copy of itself. That
    padding is low in high-frequency energy, but so are flat areas of the board,
    so we only ever trim inward from the left and right edges.
    """
    lap = np.abs(g[1:-1, 2:] + g[1:-1, :-2] - 2 * g[1:-1, 1:-1]).mean(axis=0)
    w = len(lap)
    k = max(w // 120, 3)
    prof = np.convolve(lap, np.ones(k) / k, mode="same")
    core = prof[int(w * 0.30):int(w * 0.70)]
    if core.size == 0:
        return 0, w
    thr = 0.45 * float(core.mean())
    # require a sustained run, so a bright border artifact can't anchor the span at 0
    win = max(w // 50, 8)
    sustained = np.convolve((prof > thr).astype(np.float32),
                            np.ones(win) / win, mode="same") > 0.7
    if not sustained.any():
        return 0, w
    lo = int(np.argmax(sustained))
    hi = int(w - 1 - np.argmax(sustained[::-1]))
    if lo > int(w * 0.35):
        lo = 0
    if hi < int(w * 0.65):
        hi = w - 1
    return lo, hi + 2


def analyze(path):
    im = Image.open(path).convert("RGB")
    g = np.asarray(im.convert("L"), dtype=np.float32)
    h, w = g.shape
    x0, x1 = sharp_span(g)
    cropped = False
    if (x1 - x0) < 0.92 * w and (x1 - x0) > 0.30 * w:
        im = im.crop((x0, 0, min(x1, w), h))
        cropped = True
        g = np.asarray(im.convert("L"), dtype=np.float32)
    a = np.asarray(im, dtype=np.float32)
    mx, mn = a.max(axis=2), a.min(axis=2)
    sat = float(((mx - mn) / (mx + 1e-6)).mean())
    detail = float(np.abs(g[1:-1, 2:] + g[1:-1, :-2] - 2 * g[1:-1, 1:-1]).mean())
    luma = float(g.mean())
    dark = float((g < 45).mean())
    hot = float((g > 225).mean())
    # commentary captions are large slabs of pure white; VFX cores bloom and stay tinted
    pure = float((a.min(axis=2) > 245).mean())
    return im, dict(w=im.width, h=im.height, sat=sat, detail=detail,
                    luma=luma, dark=dark, hot=hot, pure=pure, cropped=cropped)


def ahash(im):
    a = np.asarray(im.convert("L").resize((16, 16), Image.LANCZOS), dtype=np.float32)
    return (a > a.mean()).flatten()


def main():
    root = sys.argv[1]
    kept = dropped = 0
    reasons = {}
    log = []
    for cat in ("windup", "impact", "aftermath", "general"):
        d = os.path.join(root, cat)
        if not os.path.isdir(d):
            continue
        # scoped per category: a windup and its own impact are 0.28s apart and
        # would otherwise cancel each other out
        seen = []
        for f in sorted(os.listdir(d)):
            if not f.lower().endswith(".jpg"):
                continue
            p = os.path.join(d, f)
            im, m = analyze(p)
            why = None
            if max(m["w"], m["h"]) < MIN_LONG_EDGE:
                why = "too small"
            elif m["detail"] < 0.25:
                why = "flat/blurry"
            elif m["sat"] < 0.12:
                why = "desaturated (menu/text card)"
            elif m["dark"] > 0.30 or m["luma"] < 60:
                why = "dark transition/title card"
            elif m["pure"] > 0.075:
                why = "large caption text overlay"
            if why is None:
                hsh = ahash(im)
                for prev, pp in seen:
                    if int((hsh != prev).sum()) <= 12:
                        why = f"dup of {os.path.basename(pp)}"
                        break
                if why is None:
                    seen.append((hsh, p))
            if why:
                log.append(f"{cat}/{f}\t{why}\t" + " ".join(
                    f"{k}={v:.3f}" for k, v in m.items() if isinstance(v, float)))
                os.remove(p)
                dropped += 1
                reasons[why.split(" of ")[0]] = reasons.get(why.split(" of ")[0], 0) + 1
            else:
                if m["cropped"]:
                    im.save(p, quality=93)
                kept += 1
    with open(os.path.join(root, "..", "drops.tsv"), "w") as fh:
        fh.write("\n".join(log))
    print(f"kept={kept} dropped={dropped}")
    for k, v in sorted(reasons.items(), key=lambda x: -x[1]):
        print(f"  drop {k}: {v}")


if __name__ == "__main__":
    main()
