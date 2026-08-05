"""Diagnose exactly which of the four round-4 asks cost the wind-up its light."""
import numpy as np
from PIL import Image
from scipy import ndimage
import os
from _r5c_wu_measure import load, luma, hsv, S

RUNS = [(0, 512), (520, 1032), (1040, 1552)]
OUT = os.path.dirname(os.path.abspath(__file__))


def cell_pitch(img):
    """Telegraph cell pitch from the warm tile mask autocorrelation in panel 2."""
    p = img[:, RUNS[1][0]:RUNS[1][1]]
    h, s, v = hsv(p)
    warm = ((h > 15) & (h < 48) & (s > 0.22) & (v > 60)).astype(float)
    col = warm.sum(0)
    col = col - col.mean()
    ac = np.correlate(col, col, "full")[len(col) - 1:]
    # first strong peak after zero
    pk = [i for i in range(8, 260) if ac[i] > ac[i - 1] and ac[i] >= ac[i + 1] and ac[i] > 0.15 * ac[0]]
    return warm, pk[:6], ac


def telegraph_stats(img, pi):
    a, b = RUNS[pi]
    p = img[:, a:b]
    h, s, v = hsv(p)
    L = luma(p)
    m = (h > 15) & (h < 48) & (s > 0.22) & (v > 60)
    if m.sum() < 100:
        return None
    return dict(n=int(m.sum()), hue=float(np.median(h[m])), sat=float(np.median(s[m])),
                L=float(np.median(L[m])), Lmax=float(L[m].max()), smax=float(s[m].max()))


def floor_stats(img, pi):
    a, b = RUNS[pi]
    p = img[:, a:b]
    L = luma(p)
    h, s, v = hsv(p)
    # board floor: low saturation, mid-high luma, away from the caster column
    m = (s < 0.14) & (L > 150) & (L < 215)
    return float(np.median(L[m])), float(np.percentile(L[m], 25)), float(np.percentile(L[m], 75)), int(m.sum())


def charge_only(img, base, pi, hue_lo=120, hue_hi=230):
    """Cool-hued pixels that changed vs base -> the r5 charge proper."""
    a, b = RUNS[pi]
    cur, ref = img[:, a:b], base[:, a:b]
    d = np.abs(cur - ref).max(-1)
    h, s, v = hsv(cur)
    L = luma(cur)
    hr, sr, vr = hsv(ref)
    # cool-ish OR strongly brighter than base while not the pink unit / warm plate
    cool = (h > hue_lo) & (h < hue_hi)
    m = (d > 18) & (cool | ((L - luma(ref)) > 25))
    m &= ~((h > 320) | (h < 20))  # exclude the pink team body
    m = ndimage.binary_opening(m, np.ones((2, 2)))
    lab, n = ndimage.label(m, np.ones((3, 3)))
    sizes = ndimage.sum(m, lab, range(1, n + 1))
    keep = np.zeros_like(m)
    comps = []
    for j, sz in enumerate(sizes, start=1):
        if sz >= 15:
            keep |= (lab == j)
            ys, xs = np.nonzero(lab == j)
            comps.append((int(sz), int(xs.mean()), int(ys.mean()),
                          int(xs.max() - xs.min() + 1), int(ys.max() - ys.min() + 1)))
    comps.sort(reverse=True)
    return keep, comps


def describe(name, cur, mask, px_per_cell):
    L = luma(cur)
    h, s, v = hsv(cur)
    if mask.sum() == 0:
        print(f"  {name}: EMPTY")
        return
    Lv, Sv, Hv, RGB = L[mask], s[mask], h[mask], cur[mask]
    area_cells = mask.sum() / (px_per_cell ** 2)
    print(f"  {name}: {mask.sum():5d}px = {area_cells:.3f} cell^2 = {100*mask.mean():.3f}% of panel")
    print(f"      L  mean {Lv.mean():6.1f}  p90 {np.percentile(Lv,90):6.1f}  max {Lv.max():6.1f}")
    print(f"      S  mean {Sv.mean():6.3f}  p90 {np.percentile(Sv,90):6.3f}  max {Sv.max():6.3f}")
    print(f"      hue p10/p50/p90 {np.percentile(Hv,10):5.0f}/{np.percentile(Hv,50):5.0f}/{np.percentile(Hv,90):5.0f}")
    print(f"      L>200&S>=.45 {int(((Lv>200)&(Sv>=0.45)).sum()):5d}   "
          f"L>200 {int((Lv>200).sum()):5d} (Smax {Sv[Lv>200].max() if (Lv>200).any() else 0:.3f})   "
          f"S>=.45 {int((Sv>=0.45).sum()):5d} (Lmax {Lv[Sv>=0.45].max() if (Sv>=0.45).any() else 0:.0f})")
    # brightest 40 pixels raw RGB
    idx = np.argsort(Lv)[-40:]
    print(f"      brightest px mean RGB {RGB[idx].mean(0).round(1)}   "
          f"most-saturated px mean RGB {RGB[np.argsort(Sv)[-40:]].mean(0).round(1)}")
    print(f"      interior std of L (eroded core): ", end="")
    er = ndimage.binary_erosion(mask, np.ones((3, 3)), iterations=2)
    print(f"{L[er].std():.1f} over {int(er.sum())}px" if er.sum() > 30 else "core too thin to erode")


def main():
    imgs = {t: load(f"{S}/{t}/windup.jpg") for t in ("r3", "r4", "r5")}
    warm, pks, ac = cell_pitch(imgs["r5"])
    print("telegraph column-autocorrelation peaks (px):", pks)
    ppc = pks[0] if pks else 109
    print(f"--> using {ppc}px per cell on the 512px panel\n")

    for pi in (0, 1, 2):
        print(f"---------------- PANEL {pi+1} ----------------")
        fl = floor_stats(imgs["r5"], pi)
        print(f"  board floor L median {fl[0]:.1f} (p25 {fl[1]:.1f} / p75 {fl[2]:.1f}) over {fl[3]}px")
        tg = telegraph_stats(imgs["r5"], pi)
        if tg:
            print(f"  telegraph plate: hue {tg['hue']:.0f}deg  sat {tg['sat']:.2f}  L {tg['L']:.0f} "
                  f"(max {tg['Lmax']:.0f})  smax {tg['smax']:.2f}  area {tg['n']}px "
                  f"= {tg['n']/ppc**2:.2f} cell^2")
        a, b = RUNS[pi]
        m5, c5 = charge_only(imgs["r5"], imgs["r3"], pi)
        describe("r5 charge", imgs["r5"][:, a:b], m5, ppc)
        print(f"      blobs>=15px: {len(c5)}  sizes {[c[0] for c in c5[:16]]}")
        for sz, cx, cy, w, hh in c5[:6]:
            print(f"        blob {sz:5d}px at ({cx},{cy}) {w}x{hh}px = {max(w,hh)/ppc:.2f} cells long")
        np.save(f"/tmp/_r5c_m5_{pi}.npy", m5)
        print()

    print("\n================ r4 charge for comparison ================")
    for pi in (0, 1):
        a, b = RUNS[pi]
        d = np.abs(imgs["r4"][:, a:b] - imgs["r3"][:, a:b]).max(-1)
        h, s, v = hsv(imgs["r4"][:, a:b])
        m = (d > 18) & (h > 20) & (h < 90) & (s > 0.4)
        m = ndimage.binary_opening(m, np.ones((2, 2)))
        describe(f"r4 warm charge p{pi+1}", imgs["r4"][:, a:b], m, ppc)
        print()


if __name__ == "__main__":
    main()
