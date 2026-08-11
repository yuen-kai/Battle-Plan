import numpy as np
from PIL import Image
from scipy import ndimage
import os

ROOT = "/Users/cykai/Battle-Plan/Captures/AbilityJuice/shots/r2"
OUT = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"
PXCELL = {"impactdip": 213.0, "impactcore": 232.0}
CENTRE = {"impactdip": (507.0, 483.0), "impactcore": (532.0, 478.0)}

def load(shot, i):
    return np.asarray(Image.open(os.path.join(ROOT, shot, f"f{i:04d}.jpg")).convert("RGB"),
                      dtype=np.float64)
def lum(a):
    return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]

print("=" * 92)
print("M.  WHAT THE DIP DOES TO THE EFFECT'S OWN SILHOUETTE")
print("    The blue body is DARK (that is what round 2 asked for). The dip darkens the")
print("    board the body has to read against. Measure both contrasts.")
print("=" * 92)
print(f"  {'shot':>10} {'frame':>6} | {'whiteCore':>9} {'blueBody':>8} {'floorRing':>9} | "
      f"{'core-vs-floor':>13} {'body-vs-floor':>13} | {'edge step L/px':>14}")

res = {}
for shot in ("impactdip", "impactcore"):
    cy, cx = CENTRE[shot]; pc = PXCELL[shot]
    yy, xx = np.mgrid[0:1024, 0:1024]
    rr = np.hypot(yy-cy, xx-cx)/pc
    for i in (12, 13, 14, 15):
        F = load(shot, i); L = lum(F)
        mx, mn = F.max(axis=2), F.min(axis=2)
        sat = (mx-mn)/np.maximum(mx, 1e-6)
        # the saturated blue mass of the burst
        body = (sat > 0.35) & (F[...,2] > F[...,0] + 25) & (rr < 2.0)
        core = (L > 240) & (rr < 0.8)
        ring = (rr > 1.5) & (rr < 2.3) & ~body & ~core & (sat < 0.2)
        if body.sum() < 500 or ring.sum() < 500:
            continue
        cL = float(np.percentile(L[core], 50)) if core.sum() > 50 else float("nan")
        bL = float(np.median(L[body]))
        fL = float(np.median(L[ring]))
        # hardness of the body's outer edge: luminance step per pixel across the boundary
        er = ndimage.binary_dilation(body, np.ones((9,9))) & ~ndimage.binary_erosion(body, np.ones((9,9)))
        gy, gx = np.gradient(ndimage.median_filter(L, 3))
        step = float(np.percentile(np.hypot(gy, gx)[er], 90))
        res[(shot, i)] = (cL, bL, fL)
        print(f"  {shot:>10} f{i:04d} | {cL:9.1f} {bL:8.1f} {fL:9.1f} | "
              f"{cL-fL:+13.1f} {bL-fL:+13.1f} | {step:14.1f}")

print()
print("  Delta the dip is responsible for (dip minus no-dip, same frame):")
for i in (12, 13, 14, 15):
    if ("impactdip", i) in res and ("impactcore", i) in res:
        d = res[("impactdip", i)]; c = res[("impactcore", i)]
        print(f"    f{i:04d}  floor {c[2]:6.1f} -> {d[2]:6.1f} ({d[2]-c[2]:+6.1f})   "
              f"core-vs-floor {c[0]-c[2]:+6.1f} -> {d[0]-d[2]:+6.1f} ({(d[0]-d[2])-(c[0]-c[2]):+6.1f})   "
              f"BODY-vs-floor {c[1]-c[2]:+6.1f} -> {d[1]-d[2]:+6.1f} "
              f"({(d[1]-d[2])-(c[1]-c[2]):+6.1f})")

print()
print("=" * 92)
print("N.  TOTAL VALUE STRUCTURE OF THE IMPACT PANEL  (matched world window, 4x4 cells)")
print("    Dynamic range and how much of the frame is at each value.")
print("=" * 92)

def world_crop(shot, i, half_cells=2.2, size=512):
    img = load(shot, i); cy, cx = CENTRE[shot]; hp = half_cells*PXCELL[shot]
    ys = np.linspace(cy-hp, cy+hp, size); xs = np.linspace(cx-hp, cx+hp, size)
    gy, gx = np.meshgrid(ys, xs, indexing="ij")
    return np.clip(np.stack([ndimage.map_coordinates(img[...,c], [gy,gx], order=1, mode="nearest")
                             for c in range(3)], axis=-1), 0, 255)

for i in (13, 14):
    print(f"\n  frame f{i:04d} t={(i-12)*0.03333:+.2f}s")
    for shot in ("impactdip", "impactcore"):
        L = lum(world_crop(shot, i))
        p = [float(np.percentile(L, q)) for q in (1, 5, 25, 50, 75, 95, 99)]
        print(f"    {shot:>10}  p1={p[0]:5.1f} p5={p[1]:5.1f} p25={p[2]:5.1f} p50={p[3]:5.1f} "
              f"p75={p[4]:5.1f} p95={p[5]:5.1f} p99={p[6]:5.1f} | "
              f"rms={L.std():5.1f} range(p1-p99)={p[6]-p[0]:5.1f} "
              f"| %>235={100*(L>235).mean():4.1f} %<110={100*(L<110).mean():4.1f}")

print()
print("=" * 92)
print("O.  STRANGER TEST PROXY: at thumbnail size, how much does each frame differ")
print("    from its own pre-roll?  (bigger = more obviously 'something happened')")
print("=" * 92)
for shot in ("impactdip", "impactcore"):
    b = lum(world_crop(shot, 2, size=64))
    print(f"  {shot}:")
    for i in (12, 13, 14, 15, 16):
        t = lum(world_crop(shot, i, size=64))
        print(f"    f{i:04d}  meanAbsDelta={np.abs(t-b).mean():6.2f}  "
              f"rmsDelta={np.sqrt(((t-b)**2).mean()):6.2f}  darkPixels(<110)={int((t<110).sum())}/4096")

# wide A/B including the whole dip footprint
def label(a, text, h=26):
    from PIL import ImageDraw
    im = Image.fromarray(a.astype(np.uint8))
    s = Image.new("RGB", (im.width, h), (12,12,14)); ImageDraw.Draw(s).text((6,7), text, fill=(240,240,240))
    c = Image.new("RGB", (im.width, im.height+h)); c.paste(s,(0,0)); c.paste(im,(0,h))
    return np.asarray(c, dtype=np.float64)

rows = []
for i in (13, 14):
    a = label(world_crop("impactdip", i, half_cells=2.9, size=470), f"WITH DIP  f{i:04d} (5.8 cells wide)")
    b = label(world_crop("impactcore", i, half_cells=2.9, size=470), f"NO DIP    f{i:04d} (5.8 cells wide)")
    rows.append(np.hstack([a, np.full((a.shape[0], 8, 3), 18.0), b]))
w = max(r.shape[1] for r in rows)
Image.fromarray(np.vstack([np.vstack([r, np.full((8, r.shape[1], 3), 18.0)]) for r in rows]
                          ).astype(np.uint8)).save(os.path.join(OUT, "_r2_dip_ab_wide.jpg"), quality=95)
print("\nwrote _r2_dip_ab_wide.jpg")
