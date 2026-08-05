import sys, glob, json
import numpy as np
from PIL import Image
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import binary_opening, binary_closing, label, sobel


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


def clean_plate(shot, rnd="r5"):
    return np.median(np.stack([load(f) for f in frames(shot, rnd)[-12:]]), axis=0)


print("=== SHOCKWAVE: verify on the SHIPPED STRIP PANEL (512x512, what a viewer sees) ===")
for rnd in ["r4", "r5"]:
    ps = panels(load(f"{STRIPS}/{rnd}/shockwave.jpg"))
    for i, p in enumerate(ps):
        print(f"  {rnd} p{i+1}: >L240 {pct_above(p,240):6.3f}%   fully-clipped(>=250 all ch) {pct_clipped(p):6.3f}%   "
              f">=255 all ch {pct_clipped(p,255):6.3f}%   bright&sat {bright_sat_pct(p)[0]:6.3f}%")

print()
print("=== builder claims: 3.46% above L240, 0.74% fully clipped ===")
p5 = panels(load(f"{STRIPS}/r5/shockwave.jpg"))[1]
print(f"  measured on shipped impact panel: >L240 = {pct_above(p5,240):.3f}%  "
      f"fully clipped = {pct_clipped(p5):.3f}%")
print(f"  CLAIM 3.46% / 0.74%  ->  ratio {pct_above(p5,240)/3.46:.2f}x / {pct_clipped(p5)/0.74:.2f}x")

print()
print("=== raw 1024 impact frame (in case the claim was made there) ===")
picks = {q["shot"]: q for q in json.load(open(f"{STRIPS}/r5/picks.json"))}
fi = picks["shockwave"]["impact_frame"]
f = load(frames("shockwave")[fi])
print(f"  f{fi:03d}: >L240 {pct_above(f,240):.3f}%  fully clipped {pct_clipped(f):.3f}%  "
      f"bright&sat {bright_sat_pct(f)[0]:.3f}%")
print("  per-frame sweep of the whole shot (raw 1024):")
for i in range(10, 32):
    ff = load(frames("shockwave")[i])
    print(f"    f{i:03d} t={(i-12)/30.0:+.3f}s  >L240 {pct_above(ff,240):6.3f}%  clip {pct_clipped(ff):6.3f}%  "
          f"b&s {bright_sat_pct(ff)[0]:6.3f}%  peakL {lum(ff).max():5.1f}")

print()
print("=== CLAIM: centre of the blast L92 -> L252 ===")
for rnd, fidx in [("r4", None), ("r5", fi)]:
    pk = {q["shot"]: q for q in json.load(open(f"{STRIPS}/{rnd}/picks.json"))}
    idx = pk["shockwave"]["impact_frame"]
    ff = load(frames("shockwave", rnd)[idx])
    cp = clean_plate("shockwave", rnd)
    d = np.abs(ff - cp).max(axis=-1)
    m = binary_closing(binary_opening(d > 14, np.ones((3, 3))), np.ones((5, 5)))
    ys, xs = np.where(m)
    cx, cy = int(xs.mean()), int(ys.mean())
    L = lum(ff)
    # brightest-blob centroid instead (the core)
    hot = L > 235
    if hot.sum() > 50:
        hy, hx = np.where(hot)
        cx, cy = int(hx.mean()), int(hy.mean())
    patch = L[cy-16:cy+16, cx-16:cx+16]
    print(f"  {rnd} f{idx}: core centre ({cx},{cy}) 32x32 mean L {patch.mean():.1f} max {patch.max():.1f} min {patch.min():.1f}")

print()
print("=== CLAIM: the dark ring survived ===")
for rnd in ["r3", "r4", "r5"]:
    try:
        ps = panels(load(f"{STRIPS}/{rnd}/shockwave.jpg"))
    except Exception:
        continue
    p = ps[1]
    L = lum(p)
    for th in (60, 80, 110, 140):
        pass
    dark = (L < 110)
    dark_o = binary_opening(dark, np.ones((3, 3)))
    lab, n = label(dark_o)
    sizes = np.bincount(lab.ravel()); sizes[0] = 0
    print(f"  {rnd} impact panel: L<110 area {100.0*dark.sum()/dark.size:5.2f}%  "
          f"L<80 {100.0*(L<80).sum()/L.size:5.2f}%  L<40 {100.0*(L<40).sum()/L.size:5.2f}%  "
          f"largest dark blob {sizes.max() if n else 0}px")

print()
print("=== ring vs core geometry on the r5 impact panel ===")
p = panels(load(f"{STRIPS}/r5/shockwave.jpg"))[1]
L = lum(p)
hot = L > 240
hy, hx = np.where(hot)
if hot.sum() > 20:
    ccx, ccy = hx.mean(), hy.mean()
    print(f"  core centroid ({ccx:.0f},{ccy:.0f}), core area {hot.sum()}px "
          f"= {np.sqrt(hot.sum()/np.pi)*2/109:.2f} cells across (109px/cell on a 512 panel)")
    yy, xx = np.mgrid[0:512, 0:512]
    r = np.hypot(xx-ccx, yy-ccy)
    print("  radial profile from the core outward:")
    for r0 in range(0, 260, 15):
        sel = (r >= r0) & (r < r0+15)
        v = L[sel]
        dk = 100.0*(v < 110).mean()
        print(f"    r {r0:3d}-{r0+15:3d}px ({r0/109:.2f} cells)  meanL {v.mean():6.1f}  "
              f"p05 {np.percentile(v,5):6.1f}  darkfrac(L<110) {dk:5.1f}%  meanS {sat(p)[sel].mean():.3f}")

print()
print("=== CLAIM: interior gradient p99 8.1 levels/px, and the specular glint deleted ===")
for rnd in ["r4", "r5"]:
    q = panels(load(f"{STRIPS}/{rnd}/shockwave.jpg"))[1]
    Lq = lum(q)
    gx, gy = sobel(Lq, axis=1)/8.0, sobel(Lq, axis=0)/8.0
    g = np.hypot(gx, gy)
    core = Lq > 200
    core = binary_opening(core, np.ones((7, 7)))
    if core.sum() > 100:
        print(f"  {rnd}: interior (L>200) gradient  p50 {np.percentile(g[core],50):.2f}  "
              f"p90 {np.percentile(g[core],90):.2f}  p99 {np.percentile(g[core],99):.2f}  max {g[core].max():.1f}")
