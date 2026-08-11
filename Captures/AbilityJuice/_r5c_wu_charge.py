"""Isolate the wind-up charge by differencing r5/r4 against r3, then measure it."""
import numpy as np
from PIL import Image
from scipy import ndimage
import os

from _r5c_wu_measure import load, luma, hsv, S

RUNS = [(0, 512), (520, 1032), (1040, 1552)]
LABEL = ["p1", "p2", "p3"]
OUT = os.path.dirname(os.path.abspath(__file__))


def cell_px(img):
    """Estimate cell pitch in panel px from the orange telegraph tile grid in panel 2."""
    p = img[:, RUNS[1][0]:RUNS[1][1]]
    h, s, v = hsv(p)
    warm = (h > 15) & (h < 45) & (s > 0.25) & (v > 60)
    ys, xs = np.nonzero(warm)
    if len(xs) == 0:
        return None
    return warm, (xs.min(), xs.max(), ys.min(), ys.max()), warm.sum()


def analyse(tag, imgs, ref="r3"):
    print(f"\n{'='*72}\n## {tag} charge (diff vs {ref})\n{'='*72}")
    rows = []
    for i, (a, b) in enumerate(RUNS):
        cur = imgs[tag][:, a:b]
        base = imgs[ref][:, a:b]
        d = np.abs(cur - base).max(-1)
        m = d > 20
        m = ndimage.binary_opening(m, np.ones((2, 2)))
        lab, n = ndimage.label(m, np.ones((3, 3)))
        sizes = ndimage.sum(m, lab, range(1, n + 1))
        keep = np.zeros_like(m)
        comps = []
        for j, sz in enumerate(sizes, start=1):
            if sz >= 20:
                keep |= (lab == j)
                comps.append(sz)
        comps.sort(reverse=True)
        L = luma(cur)
        hh, ss, vv = hsv(cur)
        if keep.sum() == 0:
            print(f"{LABEL[i]}: empty")
            rows.append(None)
            continue
        sel = keep
        Lv = L[sel]
        Sv = ss[sel]
        Hv = hh[sel]
        # circular hue mean weighted by saturation
        w = Sv
        hm = np.degrees(np.arctan2((np.sin(np.radians(Hv)) * w).sum(), (np.cos(np.radians(Hv)) * w).sum())) % 360
        ys, xs = np.nonzero(sel)
        print(f"{LABEL[i]}: area {sel.sum():5d}px ({100*sel.mean():.3f}% of panel)  "
              f"bbox x[{xs.min()}-{xs.max()}] y[{ys.min()}-{ys.max()}]")
        print(f"     components >=20px: {len(comps)}   largest 8: {[int(c) for c in comps[:8]]}")
        print(f"     L: mean {Lv.mean():6.1f}  p50 {np.percentile(Lv,50):6.1f}  p90 {np.percentile(Lv,90):6.1f}  "
              f"p99 {np.percentile(Lv,99):6.1f}  max {Lv.max():6.1f}  std {Lv.std():5.1f}")
        print(f"     S: mean {Sv.mean():6.3f} p50 {np.percentile(Sv,50):6.3f} p90 {np.percentile(Sv,90):6.3f} "
              f" max {Sv.max():6.3f}")
        print(f"     hue (sat-weighted circular mean) {hm:6.1f}deg   "
              f"hue p25/p50/p75 {np.percentile(Hv,25):.0f}/{np.percentile(Hv,50):.0f}/{np.percentile(Hv,75):.0f}")
        bs = ((Lv > 200) & (Sv >= 0.45)).sum()
        print(f"     pixels L>200 & S>=0.45 within charge: {bs}   "
              f"L>200: {(Lv>200).sum()}   S>=0.45: {(Sv>=0.45).sum()}   "
              f"L>180: {(Lv>180).sum()}   clipped(any ch =255): {(cur[sel].max(-1)>=254).sum()}")
        # what is the max luminance among charge pixels with S>=0.45
        satm = Sv >= 0.45
        if satm.sum():
            print(f"     among S>=0.45 charge px ({satm.sum()}): Lmax {Lv[satm].max():.1f}  Lmean {Lv[satm].mean():.1f}")
        brm = Lv > 200
        if brm.sum():
            print(f"     among L>200 charge px ({brm.sum()}): Smax {Sv[brm].max():.3f}  Smean {Sv[brm].mean():.3f}")
        rows.append(dict(area=int(sel.sum()), mask=sel))
    if rows[0] and rows[1]:
        print(f"\n  >> panel2/panel1 charge area ratio = {rows[1]['area']/rows[0]['area']:.2f}x "
              f"(builder predicted 4.09x; critic asked 4-5x)")
        print(f"  >> panel1 as %% of panel2 (final) area = {100*rows[0]['area']/rows[1]['area']:.1f}%% "
              f"(critic asked 15-20%%)")
    return rows


def main():
    imgs = {t: load(f"{S}/{t}/windup.jpg") for t in ("r3", "r4", "r5")}
    r5 = analyse("r5", imgs)
    r4 = analyse("r4", imgs)

    # cell pitch from telegraph
    warm, bbox, cnt = cell_px(imgs["r5"])
    print(f"\ntelegraph warm bbox in p2: x[{bbox[0]}-{bbox[1]}] y[{bbox[2]}-{bbox[3]}]  area {cnt}px")

    # save mask overlays for eyeballing
    for tag, rows in (("r5", r5), ("r4", r4)):
        img = imgs[tag].copy()
        for i, (a, b) in enumerate(RUNS):
            if rows[i] is None:
                continue
            sub = img[:, a:b]
            edge = rows[i]["mask"] ^ ndimage.binary_erosion(rows[i]["mask"])
            sub[edge] = [255, 0, 255]
        Image.fromarray(img.astype(np.uint8)).save(f"{OUT}/_r5c_{tag}_mask.png")


if __name__ == "__main__":
    main()
