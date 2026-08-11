"""Round-2 impact core: full pixel forensics on the peak and the strip frame."""

import colorsys
import os

import numpy as np
from PIL import Image

RAW = "Captures/AbilityJuice/shots/r2/impactcore"
PPC = 217.7  # px per 2.7u cell, world-X, 1024 frame centre


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def load(i):
    return np.asarray(Image.open(os.path.join(RAW, f"f{i:04d}.jpg")).convert("RGB")).astype(np.float32)


def sat(a):
    mx = a.max(axis=-1)
    mn = a.min(axis=-1)
    return np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


clean = load(0)
cl = lum(clean)
cs = sat(clean)

print("=== BOARD FLOOR BASELINE (clean frame, 1024px) ===")
flat = cl[350:700, 250:800]
print(f"floor lum  mean {flat.mean():6.1f}  median {np.median(flat):6.1f}  p10 {np.percentile(flat,10):.1f}  p90 {np.percentile(flat,90):.1f}")
print(f"floor sat  mean {cs[350:700,250:800].mean():6.3f}  p90 {np.percentile(cs[350:700,250:800],90):.3f}")

for idx in (12, 13, 14):
    a = load(idx)
    L = lum(a)
    S = sat(a)
    print(f"\n================ FRAME f{idx:04d} ================")

    clipped = (a >= 254).all(axis=-1)
    hard255 = (a >= 255).all(axis=-1)
    print(f"peak lum {L.max():.0f}   max channel {a.max():.0f}")
    print(f"clipped px (all ch >=254): {clipped.sum():7d}  ({100*clipped.mean():.2f}% of frame)")
    print(f"clipped px (all ch ==255): {hard255.sum():7d}")

    if clipped.sum() > 50:
        ys, xs = np.nonzero(clipped)
        cy, cx = ys.mean(), xs.mean()
        w = xs.max() - xs.min() + 1
        h = ys.max() - ys.min() + 1
        eqd = 2 * np.sqrt(clipped.sum() / np.pi)
        print(f"clipped-core centroid ({cx:.0f},{cy:.0f})  bbox {w}x{h}px = {w/PPC:.2f} x {h/PPC:.2f} cells")
        print(f"clipped-core equiv diameter {eqd:.0f}px = {eqd/PPC:.2f} cells   fill {clipped.sum()/(w*h):.2f}")
    else:
        ys, xs = np.nonzero(L > 240)
        cy, cx = (ys.mean(), xs.mean()) if len(ys) else (512, 512)

    # --- ring profile from the core centroid -------------------------------
    Y, X = np.mgrid[0:1024, 0:1024]
    R = np.sqrt((X - cx) ** 2 + (Y - cy) ** 2)
    print("\n  ring(cells)   n     lum    sat    R     G     B    dLum-vs-clean")
    edges = np.arange(0, 1.85, 0.15) * PPC
    for r0, r1 in zip(edges[:-1], edges[1:]):
        m = (R >= r0) & (R < r1)
        if m.sum() == 0:
            continue
        print(f"  {r0/PPC:.2f}-{r1/PPC:.2f}  {m.sum():6d}  {L[m].mean():6.1f}  {S[m].mean():.3f}"
              f"  {a[...,0][m].mean():5.0f} {a[...,1][m].mean():5.0f} {a[...,2][m].mean():5.0f}"
              f"   {L[m].mean()-cl[m].mean():+7.1f}")

    # --- radial luminance profile: edge hardness ---------------------------
    prof = []
    for rr in range(0, 420, 3):
        m = (R >= rr) & (R < rr + 3)
        prof.append(L[m].mean() if m.sum() else np.nan)
    prof = np.array(prof)
    g = np.abs(np.diff(prof)) / 3.0
    if len(g):
        k = int(np.nanargmax(g))
        print(f"\n  steepest radial gradient {np.nanmax(g):.2f} lum/px at r={k*3}px ({k*3/PPC:.2f} cells)")
    # width of the white->blue transition
    hi = np.where(prof > 245)[0]
    lo = np.where(prof < 120)[0]
    if len(hi) and len(lo):
        r_hi = hi.max() * 3
        r_lo = lo.min() * 3 if lo.min() * 3 > r_hi else None
        if r_lo:
            print(f"  white(>245) ends r={r_hi}px ; dark(<120) begins r={r_lo}px ; ramp {r_lo-r_hi}px")

    # --- occlusion test on a tile seam under the effect --------------------
    print(f"\n  frame mean lum {L.mean():.1f} vs clean {cl.mean():.1f}  ({L.mean()-cl.mean():+.1f})")
    darker = (L < cl - 25) & (~clipped)
    print(f"  px darkened >25 below clean: {darker.sum():7d} ({100*darker.mean():.2f}%)  "
          f"equiv diameter {2*np.sqrt(max(darker.sum(),1)/np.pi)/PPC:.2f} cells")
    if darker.sum() > 100:
        print(f"  darkened region mean lum {L[darker].mean():.1f} (clean there {cl[darker].mean():.1f}), "
              f"sat {S[darker].mean():.3f}")

    # --- saturation census --------------------------------------------------
    for t in (0.30, 0.45, 0.60):
        m = S >= t
        print(f"  px with sat>={t:.2f}: {m.sum():7d}  equiv diam {2*np.sqrt(max(m.sum(),1)/np.pi)/PPC:.2f} cells"
              f"  (their mean lum {L[m].mean() if m.sum() else 0:.0f})")
