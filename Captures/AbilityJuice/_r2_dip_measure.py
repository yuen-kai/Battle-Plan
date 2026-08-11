import numpy as np
from PIL import Image
import os

ROOT = "/Users/cykai/Battle-Plan/Captures/AbilityJuice/shots/r2"

def load(shot, i, mode="RGB"):
    p = os.path.join(ROOT, shot, f"f{i:04d}.jpg")
    return np.asarray(Image.open(p).convert(mode), dtype=np.float64)

def lum(rgb):
    return 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]

for shot in ("impactdip", "impactcore"):
    a = load(shot, 2)
    print(shot, "shape", a.shape)

print()
print("=" * 78)
print("A.  CAMERA STABILITY  (is the shot static, so pixelwise comparison is valid?)")
print("=" * 78)
for shot in ("impactdip", "impactcore"):
    ref = lum(load(shot, 2))
    print(f"  {shot}:")
    for i in (0, 1, 3, 5, 8, 11, 40, 41, 42):
        d = np.abs(lum(load(shot, i)) - ref)
        print(f"    f{i:04d} vs f0002  meanAbsDelta={d.mean():7.4f}  max={d.max():6.1f}")

print()
print("=" * 78)
print("B.  IMPACT CENTRE + PEAK FRAME")
print("=" * 78)
CEN = {}
PEAK = {}
for shot in ("impactdip", "impactcore"):
    base = lum(load(shot, 2))
    best = None
    for i in range(12, 30):
        L = lum(load(shot, i))
        d = L - base
        hot = d > 40
        n = hot.sum()
        if best is None or n > best[1]:
            best = (i, n)
    pf = best[0]
    L = lum(load(shot, pf))
    d = L - lum(load(shot, 2))
    hot = d > 40
    ys, xs = np.nonzero(hot)
    w = d[hot]
    cy, cx = float((ys * w).sum() / w.sum()), float((xs * w).sum() / w.sum())
    CEN[shot] = (cy, cx)
    PEAK[shot] = pf
    print(f"  {shot}: peak-area frame f{pf:04d} (t={(pf-12)*0.03333:+.3f}s) "
          f"centre=({cx:.0f},{cy:.0f})  hotpx={best[1]}  maxL={L.max():.1f}")

print()
print("=" * 78)
print("C.  TILE SCALE  (px per cell, so radii are comparable across the two shots)")
print("=" * 78)
# tile seams are dark lines; autocorrelate a horizontal floor scanline from pre-roll
for shot in ("impactdip", "impactcore"):
    L = lum(load(shot, 2))
    row = L[700, :]          # a floor row well below the blocks
    r = row - row.mean()
    ac = np.correlate(r, r, "full")[len(r) - 1:]
    ac /= ac[0]
    # first strong peak after lag 30
    cand = [k for k in range(40, 300) if ac[k] > ac[k - 1] and ac[k] >= ac[k + 1] and ac[k] > 0.2]
    print(f"  {shot}: autocorr peaks(lag px) = {cand[:6]}")

print()
print("=" * 78)
print("D.  FLOOR LUMINANCE vs RADIUS, per frame  (the dip claim: 182 -> 120-140)")
print("=" * 78)

def floor_mask(shot):
    """Pixels that are board floor in pre-roll: bright, low-saturation, and not
    part of a block top/side or a unit. Built from the median of pre-roll frames."""
    stack = np.stack([load(shot, i) for i in range(0, 12)])
    med = np.median(stack, axis=0)
    L = lum(med)
    mx = med.max(axis=2); mn = med.min(axis=2)
    sat = (mx - mn) / np.maximum(mx, 1e-6)
    m = (L > 150) & (sat < 0.16)
    # erode a little so we don't sit on seams/edges
    from scipy import ndimage
    m = ndimage.binary_erosion(m, np.ones((3, 3)), iterations=1)
    return m, med, L

from scipy import ndimage

for shot in ("impactdip", "impactcore"):
    fm, med, baseL = floor_mask(shot)
    cy, cx = CEN[shot]
    H, W = baseL.shape
    yy, xx = np.mgrid[0:H, 0:W]
    rr = np.hypot(yy - cy, xx - cx)
    print(f"\n  --- {shot} --- floor pixels: {fm.sum()} "
          f"({100*fm.sum()/(H*W):.1f}% of frame), baseline floor L = {baseL[fm].mean():.1f}")
    bands = [(0, 60), (60, 120), (120, 200), (200, 300), (300, 420), (420, 560), (560, 999)]
    hdr = "  frame  t(s)  " + "".join(f"{lo}-{hi:<5}" for lo, hi in bands)
    print(hdr)
    for i in list(range(9, 30)) + [35, 40, 42]:
        L = lum(load(shot, i))
        cells = []
        for lo, hi in bands:
            m = fm & (rr >= lo) & (rr < hi)
            cells.append(f"{L[m].mean():7.1f}" if m.sum() > 200 else "    ---")
        print(f"  f{i:04d} {(i-12)*0.03333:+6.3f} " + "".join(cells))
