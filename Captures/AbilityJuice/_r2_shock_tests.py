"""Round-2 shockwave: the three acceptance tests the round-1 critic set, plus bloom/edge checks."""
import numpy as np
from PIL import Image

ROOT = "Captures/AbilityJuice/shots/r2/shockwave"
N = 55


def load(i):
    return np.asarray(Image.open(f"{ROOT}/f{i:04d}.jpg")).astype(np.float64)


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx = a.max(axis=-1)
    mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


F = [load(i) for i in range(N)]
L = [lum(f) for f in F]
plate = np.median(np.stack(F[0:11]), axis=0)
Lp = lum(plate)
h, w = Lp.shape
Y, X = np.mgrid[0:h, 0:w]
cx, cy, ax, ay, pxpu, CELL_PX = np.load("Captures/AbilityJuice/_r2_shock_meta.npy")
R = np.load("Captures/AbilityJuice/_r2_shock_R.npy")
t = lambda i: -0.4 + i / 30.0

print("=" * 78)
print("TILE-TO-TILE VARIATION (proper grid walk)")
print("=" * 78)
tm = []
for y0 in range(120, 900, 95):
    for x0 in range(120, 900, 95):
        blk = Lp[y0:y0 + 60, x0:x0 + 60]
        if blk.min() > 150 and blk.std() < 6:   # clean floor interior only
            tm.append(blk.mean())
tm = np.array(tm)
print(f"{len(tm)} clean floor patches: {tm.min():.1f} .. {tm.max():.1f}  "
      f"spread {tm.max()-tm.min():.1f}  sd {tm.std():.2f}")

print()
print("=" * 78)
print("TEST 1 — IS THE DUST DARKER THAN THE FLOOR? (target 70-100 vs 182)")
print("=" * 78)
print(" fr    t     dustpx   p5    p25   med    p75   mean  Δmed   minlum  sat")
for i in [12, 13, 14, 15, 16, 17, 18, 19, 20, 22, 24, 26, 28, 30, 33, 36]:
    d = L[i] - Lp
    dust = (d < -25) & (Lp > 150)          # darkened, and over floor not crates
    if dust.sum() < 200:
        print(f"{i:3d} {t(i):+.3f}   {dust.sum():6d}   -- no dust mass --")
        continue
    v = L[i][dust]
    s = sat(F[i])[dust]
    print(f"{i:3d} {t(i):+.3f}   {dust.sum():6d} {np.percentile(v,5):6.1f}{np.percentile(v,25):6.1f}"
          f"{np.median(v):6.1f}{np.percentile(v,75):6.1f}{v.mean():6.1f}  {np.median(v)-183.3:+6.1f}"
          f"  {v.min():6.1f}  {np.median(s):.3f}")

print()
print("=" * 78)
print("TEST 3a — CREST RADIUS AND AMPLITUDE PER FRAME")
print("=" * 78)
print(" fr     t    outerR(cells) darkArea(cells^2) medDark  peakBright  clip255px  rimSat")
for i in range(12, 42):
    d = L[i] - Lp
    dark = (d < -25) & (Lp > 150)
    bright = (d > 20)
    outer = R[dark].max() / 2.7 if dark.sum() > 50 else 0.0
    area = dark.sum() / (CELL_PX ** 2)
    med = np.median(L[i][dark]) if dark.sum() > 50 else float("nan")
    pk = L[i].max()
    clip = int((F[i].min(axis=2) >= 254).sum())
    rs = np.median(sat(F[i])[bright]) if bright.sum() > 50 else float("nan")
    print(f"{i:3d} {t(i):+.3f}   {outer:8.2f}     {area:10.2f}   {med:7.1f}   {pk:7.1f}   {clip:7d}   {rs:.3f}")
