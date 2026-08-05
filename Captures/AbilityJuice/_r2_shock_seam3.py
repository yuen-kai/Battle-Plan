"""TEST 2 — seam depth, per-row seam detection (the grid is in perspective, so a seam is not a
fixed column). Depth = local linear trend of the flanking tile faces minus the seam trough, which
removes the effect's own gradient and the rim from the measurement."""
import numpy as np
from PIL import Image

ROOT = "Captures/AbilityJuice/shots/r2/shockwave"
lum = lambda a: 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
load = lambda i: np.asarray(Image.open(f"{ROOT}/f{i:04d}.jpg")).astype(np.float64)

plate = np.median(np.stack([load(i) for i in range(11)]), axis=0)
Lp = lum(plate)
h, w = Lp.shape
HALF = 10
cx, cy, axx, ayy = 511.5, 538.5, 302.5, 290.5


def depth_at(Larr, y, sx):
    seg = Larr[y, sx - HALF:sx + HALF + 1]
    if seg.size < 2 * HALF + 1:
        return None
    trend = (seg[0:4].mean() + seg[-4:].mean()) / 2.0
    return trend - seg[HALF - 2:HALF + 3].min()


# ---- find seam pixels on the clean plate: per-row local minima on floor ---------------
seam_pts = []
for y in range(280, 820):
    row = Lp[y]
    for x in range(HALF + 2, w - HALF - 2):
        seg = row[x - HALF:x + HALF + 1]
        if seg[0:4].mean() < 165 or seg[-4:].mean() < 165:
            continue                                   # flanking faces must be clean floor
        if seg.min() < 100:
            continue                                   # crate/prop shadow in the window
        if row[x] != row[x - 4:x + 5].min():
            continue
        d = depth_at(Lp, y, x)
        if d is not None and d > 25:
            r = np.sqrt(((x - cx) / axx) ** 2 + ((y - cy) / ayy) ** 2) * 4.3 / 2.7
            seam_pts.append((y, x, d, r))
            
# dedupe: keep one sample per (row, seam) — suppress neighbours within 6px
seam_pts.sort()
kept = []
for p in seam_pts:
    if kept and kept[-1][0] == p[0] and p[1] - kept[-1][1] < 6:
        continue
    kept.append(p)
seam_pts = kept
dep = np.array([p[2] for p in seam_pts])
print("=" * 78)
print("CLEAN FLOOR SEAM DEPTH")
print("=" * 78)
print(f"{len(seam_pts)} seam samples across the board")
print(f"mean {dep.mean():.1f}  median {np.median(dep):.1f}  p25 {np.percentile(dep,25):.1f} "
      f" p75 {np.percentile(dep,75):.1f}  max {dep.max():.1f}")
inner = [p for p in seam_pts if p[3] < 1.7]
di = np.array([p[2] for p in inner])
print(f"within the blast footprint (r<1.7 cells): {len(inner)} samples, mean {di.mean():.1f}, "
      f"median {np.median(di):.1f}")

print()
print("=" * 78)
print("TEST 2 — SEAM DEPTH UNDER DUST   (round 1: 38.6 clean -> 39.0 through. target <8)")
print("=" * 78)
print(" fr     t     n_under   clean_of_those   under_dust   median   p90    %>8")
for fr in [12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 24, 26, 28, 30, 33]:
    Lf = lum(load(fr))
    a, b = [], []
    for (y, x, d, r) in seam_pts:
        if Lf[y, x] - Lp[y, x] < -25:
            v = depth_at(Lf, y, x)
            if v is not None:
                a.append(v)
                b.append(d)
    if len(a) < 20:
        print(f"{fr:3d} {-0.4+fr/30:+.3f}   {len(a):6d}   -- too few --")
        continue
    a = np.array(a)
    b = np.array(b)
    print(f"{fr:3d} {-0.4+fr/30:+.3f}   {len(a):6d}       {b.mean():6.1f}       {a.mean():7.1f}"
          f"  {np.median(a):7.1f} {np.percentile(a,90):6.1f}  {(a>8).mean()*100:5.1f}%")

# ---- the single named seam, tracked ---------------------------------------------------
print()
print("=" * 78)
print("A SINGLE NAMED SEAM, TRACKED FRAME BY FRAME")
print("=" * 78)
band = [p for p in seam_pts if 560 <= p[0] <= 600 and 380 <= p[1] <= 460]
if not band:
    band = sorted(seam_pts, key=lambda p: abs(p[3] - 1.0))[:30]
ys = [(p[0], p[1]) for p in band]
c = np.mean([p[2] for p in band])
print(f"seam pixels rows {min(p[0] for p in band)}-{max(p[0] for p in band)}, "
      f"x~{int(np.mean([p[1] for p in band]))}, r~{np.mean([p[3] for p in band]):.2f} cells")
print(f"CLEAN depth {c:.1f}")
for fr in [13, 15, 17, 19, 22, 26, 30, 33]:
    Lf = lum(load(fr))
    v = np.mean([depth_at(Lf, y, x) for (y, x) in ys])
    u = sum(1 for (y, x) in ys if Lf[y, x] - Lp[y, x] < -25)
    print(f"  f{fr:2d} t={-0.4+fr/30:+.3f}   depth {v:6.1f}   ({u}/{len(ys)} under dust)")
