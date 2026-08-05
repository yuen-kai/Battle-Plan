"""Is bloom reaching the board? Where does the effect terminate? Shard cross-section."""

import os

import numpy as np
from PIL import Image

RAW = "Captures/AbilityJuice/shots/r2/impactcore"
PPC = 217.7
CX, CY = 512, 493


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def load(i):
    return np.asarray(Image.open(os.path.join(RAW, f"f{i:04d}.jpg")).convert("RGB")).astype(np.float32)


clean = load(0)
cl = lum(clean)
Y, X = np.mgrid[0:1024, 0:1024]
R = np.sqrt((X - CX) ** 2 + (Y - CY) ** 2)

print("=== RADIAL DELTA vs CLEAN — where does the effect stop? ===")
print("cells    f0012      f0013      f0014      (mean signed delta; +=brighter)")
for c0 in np.arange(0.8, 3.1, 0.1):
    m = (R >= c0 * PPC) & (R < (c0 + 0.1) * PPC)
    if m.sum() < 200:
        continue
    row = "  ".join(f"{(lum(load(i))[m] - cl[m]).mean():+8.2f}" for i in (12, 13, 14))
    print(f"{c0:4.1f}   {row}")

print("\n=== POSITIVE-ONLY LIFT (a bloom halo would show here) ===")
for i in (12, 13, 14):
    L = lum(load(i))
    d = L - cl
    for c0, c1 in ((2.0, 2.3), (2.3, 2.6), (2.6, 3.0)):
        m = (R >= c0 * PPC) & (R < c1 * PPC)
        pos = d[m][d[m] > 0]
        print(f"  f{i:04d} ring {c0}-{c1}c: mean {d[m].mean():+6.2f}  "
              f"95th pct {np.percentile(d[m],95):+6.2f}  max {d[m].max():+6.1f}  "
              f"frac lifted>3: {(d[m] > 3).mean()*100:5.1f}%")

print("\n=== SHARD CROSS-SECTION (f0013): perpendicular profile through a floor shard ===")
a = load(13)
L = lum(a)
d = L - cl
# the 119.7deg shard sits up-left; sample a horizontal cut across it
best = None
for row in range(300, 400):
    seg = d[row, 300:470]
    if seg.max() > 30:
        w = (seg > 15).sum()
        if 4 < w < 90 and (best is None or seg.max() > best[1]):
            best = (row, seg.max(), w)
if best:
    row = best[0]
    print(f"  row y={row}:")
    vals = [(x, L[row, x], cl[row, x], d[row, x]) for x in range(300, 470, 4)]
    for x, v, c, dd in vals:
        bar = "#" * max(0, int(dd / 3))
        print(f"    x={x:4d} lum {v:6.1f} clean {c:6.1f} d {dd:+6.1f} {bar}")

print("\n=== OPAQUE SILHOUETTE (strict blue) PER FRAME ===")
print("frame  strict-blue eqdiam   90th-pct radius   outer radius where |d|<3")
for i in range(12, 18):
    a = load(i)
    L = lum(a)
    strict = (a[..., 2] - a[..., 0] > 90) & (L < cl - 60)
    ys, xs = np.nonzero(strict)
    if len(xs) < 100:
        print(f"f{i:04d}  (none)")
        continue
    rr = np.sqrt((xs - CX) ** 2 + (ys - CY) ** 2)
    dd = np.abs(L - cl)
    edge = 0
    for c0 in np.arange(0.5, 3.0, 0.05):
        m = (R >= c0 * PPC) & (R < (c0 + 0.05) * PPC)
        if m.sum() > 200 and dd[m].mean() < 3:
            edge = c0
            break
    print(f"f{i:04d}  {2*np.sqrt(strict.sum()/np.pi)/PPC:12.2f}c   {np.percentile(rr,90)/PPC:12.2f}c"
          f"   {edge:15.2f}c")
