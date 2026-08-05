"""Edge hardness, hue, rim character, aftermath drift, and the seam test restricted to windows
that are wholly inside the dust (removing the boundary artefact)."""
import numpy as np
from PIL import Image

ROOT = "Captures/AbilityJuice/shots/r2/shockwave"
lum = lambda a: 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
load = lambda i: np.asarray(Image.open(f"{ROOT}/f{i:04d}.jpg")).astype(np.float64)
plate = np.median(np.stack([load(i) for i in range(11)]), axis=0)
Lp = lum(plate)
h, w = Lp.shape
Y, X = np.mgrid[0:h, 0:w]
cx, cy, axx, ayy = 511.5, 538.5, 302.5, 290.5
R = np.sqrt(((X - cx) / axx) ** 2 + ((Y - cy) / ayy) ** 2) * 4.3 / 2.7   # cells
HALF = 10


def sat(a):
    mx = a.max(axis=-1)
    mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


print("=" * 78)
print("TEST 2b — SEAM DEPTH, WINDOW WHOLLY UNDER DUST (no boundary straddle)")
print("=" * 78)
seam_pts = []
for y in range(280, 820):
    row = Lp[y]
    for x in range(HALF + 2, w - HALF - 2):
        seg = row[x - HALF:x + HALF + 1]
        if seg[0:4].mean() < 165 or seg[-4:].mean() < 165 or seg.min() < 100:
            continue
        if row[x] != row[x - 4:x + 5].min():
            continue
        trend = (seg[0:4].mean() + seg[-4:].mean()) / 2.0
        d = trend - seg[HALF - 2:HALF + 3].min()
        if d > 25:
            seam_pts.append((y, x, d))
kept = []
for p in sorted(seam_pts):
    if kept and kept[-1][0] == p[0] and p[1] - kept[-1][1] < 6:
        continue
    kept.append(p)
seam_pts = kept

for fr in [15, 17, 18, 19, 22, 24, 26, 28, 30]:
    Ff = load(fr)
    Lf = lum(Ff)
    D = Lf - Lp
    a, b = [], []
    for (y, x, d) in seam_pts:
        win = D[y, x - HALF:x + HALF + 1]
        if win.max() < -25:                                  # every pixel of the window occluded
            seg = Lf[y, x - HALF:x + HALF + 1]
            trend = (seg[0:4].mean() + seg[-4:].mean()) / 2.0
            a.append(trend - seg[HALF - 2:HALF + 3].min())
            b.append(d)
    if len(a) < 20:
        print(f"f{fr:2d} t={-0.4+fr/30:+.3f}   n={len(a)} too few")
        continue
    a, b = np.array(a), np.array(b)
    print(f"f{fr:2d} t={-0.4+fr/30:+.3f}   n={len(a):5d}   clean {b.mean():5.1f} -> under dust "
          f"{a.mean():5.1f}  (median {np.median(a):4.1f}, p90 {np.percentile(a,90):5.1f}, "
          f"{(a>8).mean()*100:4.1f}% still >8)   kill {100*(1-a.mean()/b.mean()):.0f}%")

print()
print("=" * 78)
print("EDGE HARDNESS — luminance step per pixel across the front's outer boundary")
print("=" * 78)
print("(round 1 ours: 0.9 L/px over 16px.  Clash Mini dust silhouettes: 49-91 L/px)")
for fr in [14, 15, 16, 17, 18]:
    Lf = lum(load(fr))
    D = Lf - Lp
    grads = []
    widths = []
    for ang in np.linspace(0, 2 * np.pi, 180, endpoint=False):
        prof, rr = [], []
        for t in np.arange(0.6, 2.1, 0.01):
            px = int(round(cx + np.cos(ang) * t * 2.7 / 4.3 * axx))
            py = int(round(cy + np.sin(ang) * t * 2.7 / 4.3 * ayy))
            if 0 <= px < w and 0 <= py < h and Lp[py, px] > 150:
                prof.append(D[py, px])
                rr.append(t)
        if len(prof) < 40:
            continue
        prof = np.array(prof)
        if prof.min() > -35:
            continue
        i0 = int(np.argmin(prof))
        # walk outward from the darkest point to where the darkening ends
        j = i0
        while j < len(prof) - 1 and prof[j] < -8:
            j += 1
        drop = abs(prof[i0])
        px_span = max(1, (rr[j] - rr[i0]) * (4.3 / 2.7) * (axx / 4.3))
        grads.append(drop / px_span)
        widths.append(px_span)
    if grads:
        print(f"f{fr:2d} t={-0.4+fr/30:+.3f}  outer edge: {np.median(grads):5.1f} L/px  "
              f"(median transition width {np.median(widths):4.1f} px, n={len(grads)} rays)")

print()
print("=" * 78)
print("HUE — is the dust warm against a cool board?")
print("=" * 78)
fl = (Lp > 150) & (R < 3)
print(f"board floor    R{plate[...,0][fl].mean():6.1f} G{plate[...,1][fl].mean():6.1f} "
      f"B{plate[...,2][fl].mean():6.1f}   B-R {plate[...,2][fl].mean()-plate[...,0][fl].mean():+6.1f}"
      f"   sat {sat(plate)[fl].mean():.3f}")
for fr in [15, 17, 19, 24, 30]:
    Ff = load(fr)
    d = lum(Ff) - Lp
    m = (d < -40) & (Lp > 150)
    if m.sum() < 500:
        continue
    print(f"dust f{fr:2d}       R{Ff[...,0][m].mean():6.1f} G{Ff[...,1][m].mean():6.1f} "
          f"B{Ff[...,2][m].mean():6.1f}   B-R {Ff[...,2][m].mean()-Ff[...,0][m].mean():+6.1f}"
          f"   sat {sat(Ff)[m].mean():.3f}   lum {lum(Ff)[m].mean():.1f}")

print()
print("=" * 78)
print("RIM — clipped-white area, hue, and whether any saturated colour survives")
print("=" * 78)
for fr in [12, 13, 14, 15, 16, 17, 18, 19]:
    Ff = load(fr)
    Lf = lum(Ff)
    hot = Lf > 230
    clip = Ff.min(axis=2) >= 254
    if hot.sum() < 50:
        print(f"f{fr:2d} t={-0.4+fr/30:+.3f}  no hot pixels")
        continue
    s = sat(Ff)[hot]
    print(f"f{fr:2d} t={-0.4+fr/30:+.3f}  hot(L>230) {hot.sum():6d}px = {hot.sum()/190**2:.3f} cells^2"
          f" | fully clipped {clip.sum():6d}px | hot sat mean {s.mean():.3f} p90 {np.percentile(s,90):.3f}"
          f" | any px sat>0.45 & L>200: {int(((sat(Ff)>0.45)&(Lf>200)).sum())}")

print()
print("=" * 78)
print("AFTERMATH DRIFT — is anything changing between +0.33s and +0.60s?")
print("=" * 78)
print(" fr     t     area(cells^2)  bbox w x h (cells)  centroid drift from prev (cells)  medL")
prev = None
for fr in range(20, 35):
    Lf = lum(load(fr))
    m = (Lf - Lp < -25) & (Lp > 150)
    if m.sum() < 100:
        print(f"{fr:3d} {-0.4+fr/30:+.3f}   empty")
        prev = None
        continue
    ys, xs = np.where(m)
    ccx, ccy = xs.mean(), ys.mean()
    bw = (xs.max() - xs.min()) / 190
    bh = (ys.max() - ys.min()) / 190
    dr = np.hypot(ccx - prev[0], ccy - prev[1]) / 190 if prev else 0
    print(f"{fr:3d} {-0.4+fr/30:+.3f}   {m.sum()/190**2:8.2f}      {bw:.2f} x {bh:.2f}"
          f"           {dr:.3f}                 {np.median(Lf[m]):.1f}")
    prev = (ccx, ccy)
