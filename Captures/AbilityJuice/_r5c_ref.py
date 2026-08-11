"""What hue/sat/luma combos do the Clash Mini references actually use for bright+saturated pixels?"""
import numpy as np
from PIL import Image
from scipy import ndimage
import os, glob
from _r5c_wu_measure import load, luma, hsv, S

REF = f"{S}/reference"


def panel_runs(img):
    L = luma(img)
    dark = L.mean(0) < 30
    runs, st = [], None
    for i, d in enumerate(dark):
        if not d and st is None:
            st = i
        elif d and st is not None:
            runs.append((st, i)); st = None
    if st is not None:
        runs.append((st, len(dark)))
    return [r for r in runs if r[1] - r[0] > 50]


def main():
    files = sorted(glob.glob(f"{REF}/*.jpg"))
    print(f"{'file':30s} {'panel':>5s} {'BS%':>7s} {'hue(BS)':>8s} {'Lbs':>6s} {'Sbs':>6s} "
          f"{'clip%':>6s} {'floorL':>7s}")
    allbs = []
    warmc, coolc = 0, 0
    for f in files:
        img = load(f)
        for pi, (a, b) in enumerate(panel_runs(img)):
            p = img[:, a:b]
            L = luma(p); h, s, v = hsv(p)
            m = (L > 200) & (s >= 0.45)
            pct = 100 * m.mean()
            allbs.append(pct)
            clip = 100 * ((p.max(-1) >= 250).mean())
            fl = np.median(L[(s < 0.20)]) if (s < 0.20).any() else float("nan")
            if m.sum() > 30:
                hv = h[m]
                hm = np.degrees(np.arctan2(np.sin(np.radians(hv)).mean(),
                                           np.cos(np.radians(hv)).mean())) % 360
                warm = ((hv < 70) | (hv > 330)).mean()
                if warm > 0.5: warmc += 1
                else: coolc += 1
                print(f"{os.path.basename(f):30s} {pi+1:5d} {pct:7.3f} {hm:8.0f} "
                      f"{L[m].mean():6.0f} {s[m].mean():6.2f} {clip:6.2f} {fl:7.1f}   warm-frac {warm:.2f}")
            else:
                print(f"{os.path.basename(f):30s} {pi+1:5d} {pct:7.3f} {'-':>8s} "
                      f"{'-':>6s} {'-':>6s} {clip:6.2f} {fl:7.1f}")
    a = np.array(allbs)
    print(f"\nreference panels n={len(a)}  BS%%: min {a.min():.3f} p25 {np.percentile(a,25):.3f} "
          f"median {np.median(a):.3f} p75 {np.percentile(a,75):.3f} max {a.max():.3f}")
    print(f"panels with BS >= 0.30%: {(a>=0.30).sum()}/{len(a)}   >=1.0%: {(a>=1.0).sum()}/{len(a)}")
    print(f"dominant-hue class of BS pixels: warm panels {warmc}, cool panels {coolc}")

    # ceiling curves: max achievable luma at V=1 for each hue/sat
    print("\n### 8-bit ceiling: max luminance at V=255 for a given hue & saturation")
    print(f"{'hue':>5s} " + " ".join(f"S={s:.2f}" for s in (0.45, 0.60, 0.80, 1.00)))
    import colorsys
    for hdeg in (0, 10, 15, 20, 25, 30, 33, 40, 45, 52, 60, 90, 150, 160, 165, 175, 185, 190, 195, 200, 210, 240):
        row = []
        for sat in (0.45, 0.60, 0.80, 1.00):
            r, g, bb = colorsys.hsv_to_rgb(hdeg / 360.0, sat, 1.0)
            row.append(f"{255*(0.2126*r+0.7152*g+0.0722*bb):5.0f}")
        star = "  <-- telegraph" if hdeg == 33 else ("  <-- r5 charge" if hdeg == 165 else
              ("  <-- r4 yellow" if hdeg == 52 else ""))
        print(f"{hdeg:5d} " + " ".join(row) + star)


if __name__ == "__main__":
    main()
