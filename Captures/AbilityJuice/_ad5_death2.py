import sys, glob, json
import numpy as np
from PIL import Image
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import binary_opening, binary_closing, maximum_filter, minimum_filter, label


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


def clean_plate(shot, rnd="r5"):
    return np.median(np.stack([load(f) for f in frames(shot, rnd)[-12:]]), axis=0)


def emask(f, cp, thresh=14):
    d = np.abs(f - cp).max(axis=-1)
    m = d > thresh
    return binary_closing(binary_opening(m, np.ones((3, 3))), np.ones((5, 5)))


print("=== CLAIM: fraction of effect mass below L40, r4 0.69 -> r5 0.25 ===")
for rnd, fi in [("r4", None), ("r5", 18)]:
    picks = {p["shot"]: p for p in json.load(open(f"{STRIPS}/{rnd}/picks.json"))}
    fidx = picks["death"]["impact_frame"]
    cp = clean_plate("death", rnd)
    f = load(frames("death", rnd)[fidx])
    m = emask(f, cp)
    L = lum(f)[m]
    print(f"  {rnd} impact f{fidx}: effect px {m.sum()}, L<40 = {(L<40).mean():.3f}, "
          f"L<80 = {(L<80).mean():.3f}, L<110 = {(L<110).mean():.3f}, meanL {L.mean():.1f}")

print()
print("=== CLAIM: vertical profile 221 at top edge -> 6 at centre-bottom ===")
cp = clean_plate("death")
f = load(frames("death")[18])
m = emask(f, cp)
L = lum(f)
ys, xs = np.where(m)
y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
cx = int(np.round(xs.mean()))
print(f"  effect bbox y[{y0}..{y1}] x[{x0}..{x1}]  centroid x={cx}")
print("  row-wise stats INSIDE the effect mask only:")
rows = np.linspace(y0, y1, 16).astype(int)
for y in rows:
    sel = m[y]
    if sel.sum() < 5:
        print(f"    y={y:4d}  (n<5)")
        continue
    v = L[y][sel]
    print(f"    y={y:4d} n={sel.sum():4d}  meanL {v.mean():6.1f}  minL {v.min():6.1f}  maxL {v.max():6.1f}  p95 {np.percentile(v,95):6.1f}")

print()
print("  --- 'hole' test: is the centre deeper than the rim? ---")
# radial profile from the darkest core outward
Lm = np.where(m, L, np.nan)
# centroid of the DARK part
dm = m & (L < 60)
if dm.sum() > 20:
    dys, dxs = np.where(dm)
    ccx, ccy = dxs.mean(), dys.mean()
else:
    ccx, ccy = xs.mean(), ys.mean()
yy, xx = np.mgrid[0:L.shape[0], 0:L.shape[1]]
r = np.hypot(xx - ccx, yy - ccy)
print(f"  dark-core centroid ({ccx:.0f},{ccy:.0f}); radial mean L inside mask:")
for r0 in range(0, 320, 20):
    sel = m & (r >= r0) & (r < r0 + 20)
    if sel.sum() > 20:
        print(f"    r {r0:3d}-{r0+20:3d}px ({r0/218:.2f}-{(r0+20)/218:.2f} cells) n={sel.sum():5d} "
              f"meanL {L[sel].mean():6.1f}  p05 {np.percentile(L[sel],5):6.1f}  p95 {np.percentile(L[sel],95):6.1f}  "
              f"meanS {sat(f)[sel].mean():.2f}")

print()
print("=== CLAIM: interior local contrast 1.54 -> 6.55 ===")
for rnd in ["r4", "r5"]:
    picks = {p["shot"]: p for p in json.load(open(f"{STRIPS}/{rnd}/picks.json"))}
    fidx = picks["death"]["impact_frame"]
    cpx = clean_plate("death", rnd)
    fx = load(frames("death", rnd)[fidx])
    mx = emask(fx, cpx)
    Lx = lum(fx)
    lc = maximum_filter(Lx, 9) - minimum_filter(Lx, 9)
    core = binary_opening(mx, np.ones((11, 11)))  # erode to interior only
    print(f"  {rnd}: interior 9x9 local contrast mean {lc[core].mean():.2f}  "
          f"std of L {Lx[core].std():.2f}  (n={core.sum()})")

print()
print("=== CLAIM: five visible corpse frames (r4 had two) ===")
print("  looking for a unit-shaped dark mass distinct from the effect, per frame")
for rnd in ["r4", "r5"]:
    cpx = clean_plate("death", rnd)
    fs = frames("death", rnd)
    out = []
    for i in range(10, 34):
        fx = load(fs[i])
        mx = emask(fx, cpx)
        Lx, Sx = lum(fx), sat(fx)
        # "corpse" = desaturated dark material (the unit is pink/black; effect is cyan)
        H = hue(fx)
        corpse = mx & (Lx < 110) & ((Sx < 0.25) | ((H > 300) | (H < 30)))
        corpse = binary_opening(corpse, np.ones((5, 5)))
        out.append((i, corpse.sum()))
    vis = [i for i, c in out if c > 900]
    print(f"  {rnd}: frames with >900px of desaturated/pink dark mass: {vis}  (count {len(vis)})")
    print(f"       raw: " + " ".join(f"{i}:{c}" for i, c in out))
