"""Shards, edge hardness, internal structure, shrink curve, light spill."""

import os

import numpy as np
from PIL import Image
from scipy import ndimage

RAW = "Captures/AbilityJuice/shots/r2/impactcore"
PPC = 217.7
CX, CY = 512, 493


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def load(i):
    return np.asarray(Image.open(os.path.join(RAW, f"f{i:04d}.jpg")).convert("RGB")).astype(np.float32)


clean = load(0)
cl = lum(clean)

# ---------------------------------------------------------------- shrink curve
print("=== BLOB EXTENT PER FRAME (does it expand or contract?) ===")
print("frame   blue-mass eqdiam   white-core eqdiam   blue max radius")
for i in range(11, 19):
    a = load(i)
    L = lum(a)
    blue = (a[..., 2] - a[..., 0] > 25) & (L < cl - 15)
    white = (a >= 254).all(axis=-1)
    lab, n = ndimage.label(blue)
    if n:
        sizes = ndimage.sum(blue, lab, range(1, n + 1))
        big = blue & (lab == (np.argmax(sizes) + 1))
    else:
        big = blue
    ys, xs = np.nonzero(big)
    rad = np.sqrt((xs - CX) ** 2 + (ys - CY) ** 2).max() if len(xs) else 0
    bd = 2 * np.sqrt(max(big.sum(), 1) / np.pi) / PPC
    wd = 2 * np.sqrt(max(white.sum(), 1) / np.pi) / PPC
    print(f"f{i:04d}   {bd:8.2f} cells    {wd:9.2f} cells     {rad/PPC:6.2f} cells")

# ------------------------------------------------------- edge hardness, f0013
print("\n=== EDGE HARDNESS (f0013, 8 radial rays from centre) ===")
a = load(13)
L = lum(a)
print("ray   white->blue step (lum/px)   blue->floor step (lum/px)   outer radius")
for k in range(8):
    th = k * np.pi / 4
    dx, dy = np.cos(th), np.sin(th)
    prof = np.array([L[int(CY + dy * r), int(CX + dx * r)]
                     for r in range(0, 460) if 0 <= int(CY + dy * r) < 1024 and 0 <= int(CX + dx * r) < 1024])
    d = np.diff(prof)
    inner = np.abs(d[:200]).max() if len(d) > 200 else 0
    # outer boundary: last place the profile climbs back toward the floor
    outer_i = None
    for r in range(len(prof) - 3, 60, -1):
        if prof[r - 1] < 165 <= prof[r] or (prof[r] > 168 and prof[r - 1] < 150):
            outer_i = r
            break
    outer_slope = 0
    if outer_i:
        w = prof[max(0, outer_i - 40): outer_i + 10]
        outer_slope = np.abs(np.diff(w)).max() if len(w) > 2 else 0
        # 10-90 rise width
        lo, hi = w.min(), w.max()
        band = np.where((w > lo + 0.1 * (hi - lo)) & (w < lo + 0.9 * (hi - lo)))[0]
        width = (band.max() - band.min() + 1) if len(band) else 0
    else:
        width = -1
    print(f" {k*45:3d}deg      {inner:8.2f}                 {outer_slope:8.2f}            "
          f"r={outer_i if outer_i else -1}px 10-90 over {width}px")

# ------------------------------------------------- internal structure of blue
print("\n=== INTERNAL STRUCTURE (spread inside the blue mass) ===")
for i in (12, 13, 14):
    a = load(i)
    L = lum(a)
    blue = (a[..., 2] - a[..., 0] > 40) & (L < cl - 25)
    # exclude the radial-shell trend: measure spread within thin annuli
    Y, X = np.mgrid[0:1024, 0:1024]
    R = np.sqrt((X - CX) ** 2 + (Y - CY) ** 2)
    spreads = []
    for r in range(40, 420, 12):
        m = blue & (R >= r) & (R < r + 12)
        if m.sum() > 400:
            spreads.append(L[m].std())
    print(f"f{i:04d}: mean within-annulus luminance std = {np.mean(spreads):.2f} "
          f"(n={len(spreads)} annuli)   [Clash puffs measure 30-45]")

# -------------------------------------------------------------- shard census
print("\n=== SHARDS (f0013): pale wedges outside the blue mass ===")
a = load(13)
L = lum(a)
shard = (L > cl + 8) & ((a >= 254).all(axis=-1) == False)
Y, X = np.mgrid[0:1024, 0:1024]
R = np.sqrt((X - CX) ** 2 + (Y - CY) ** 2)
shard &= R > 120
lab, n = ndimage.label(shard, structure=np.ones((3, 3)))
sizes = ndimage.sum(shard, lab, range(1, n + 1))
keep = [k for k in np.argsort(sizes)[::-1] if sizes[k] > 250][:12]
print(f"{len(keep)} shard blobs >250px (of {n} components)")
for k in keep:
    m = lab == (k + 1)
    ys, xs = np.nonzero(m)
    rr = np.sqrt((xs - CX) ** 2 + (ys - CY) ** 2)
    ang = np.degrees(np.arctan2(ys.mean() - CY, xs.mean() - CX)) % 360
    # tip contrast: brightest 5% of the blob vs clean floor there
    v = L[m]
    print(f"  {int(sizes[k]):5d}px  reach {rr.max()/PPC:.2f}c  len {(rr.max()-rr.min())/PPC:.2f}c  "
          f"ang {ang:5.1f}deg  meanLum {v.mean():5.1f} (floor {cl[m].mean():5.1f}, "
          f"delta {v.mean()-cl[m].mean():+5.1f})  maxLum {v.max():.0f}")

# ----------------------------------------------------------- light spill test
print("\n=== LIGHT SPILL: does the world respond? ===")
# nearest crates in the clean frame: dark blobs
dark = cl < 110
lab, n = ndimage.label(dark)
sizes = ndimage.sum(dark, lab, range(1, n + 1))
for k in np.argsort(sizes)[::-1][:5]:
    m = lab == (k + 1)
    ys, xs = np.nonzero(m)
    d = np.sqrt((xs.mean() - CX) ** 2 + (ys.mean() - CY) ** 2) / PPC
    row = [f"f{i:04d} {lum(load(i))[m].mean():6.2f}" for i in (0, 12, 13, 14)]
    print(f"  crate {int(sizes[k]):6d}px at {d:.2f} cells:  " + "  ".join(row))

# floor annulus just outside the blue
Y, X = np.mgrid[0:1024, 0:1024]
R = np.sqrt((X - CX) ** 2 + (Y - CY) ** 2)
for lo, hi in ((1.9, 2.1), (2.1, 2.3)):
    m = (R >= lo * PPC) & (R < hi * PPC)
    vals = [f"f{i:04d} {lum(load(i))[m].mean():6.2f}" for i in (0, 12, 13, 14)]
    print(f"  floor ring {lo}-{hi} cells:  " + "  ".join(vals))
