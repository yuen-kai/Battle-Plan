import sys
import numpy as np
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import maximum_filter, minimum_filter, label, binary_opening

d5 = panels(load(f"{STRIPS}/r5/death.jpg"))
d4 = panels(load(f"{STRIPS}/r4/death.jpg"))
pg = panels(load(f"{STRIPS}/r5/pogo.jpg"))

print("=== builder claim: 1.76% bright-and-saturated. Try every plausible convention ===")
p = d5[1]
for lname, Lf in [("rec601", lambda q: lum(q)),
                  ("rec709", lambda q: 0.2126*q[...,0]+0.7152*q[...,1]+0.0722*q[...,2]),
                  ("HSV V (max)", lambda q: q.max(axis=-1)),
                  ("mean RGB", lambda q: q.mean(axis=-1))]:
    L = Lf(p)
    S = sat(p)
    for lt, st in [(199, 0.45), (200, 0.45), (180, 0.40), (199, 0.40)]:
        pc = 100.0*((L > lt) & (S > st)).sum()/L.size
        print(f"  {lname:12s} L>{lt} S>{st}: {pc:6.3f}%")

print()
print("=== whole strip vs panel, r5 death ===")
full = load(f"{STRIPS}/r5/death.jpg")
print(f"  whole strip incl. separators: {bright_sat_pct(full)[0]:.3f}%")
for i, q in enumerate(d5):
    print(f"  panel {i+1}: {bright_sat_pct(q)[0]:.3f}%")

# --- effect isolation: difference against panel 3 board (closest to clean) ---
print()
print("=== dark-mass fraction: pixels below L40 ===")
for tag, ps in [("r4", d4), ("r5", d5), ("pogo r5", pg)]:
    out = []
    for q in ps:
        L = lum(q)
        out.append(100.0*(L < 40).sum()/L.size)
    print(f"  {tag:8s} " + " ".join(f"{v:6.3f}%" for v in out))

print()
print("=== effect-region isolation (diff vs board) ===")
# Use panel1 of numbers strip? No - use a clean-board proxy: median of the three panels
# per pixel is a poor proxy. Instead segment by 'not board': board is desaturated bright grey.
def effect_mask(q):
    L, S = lum(q), sat(q)
    # board floor is bright & desaturated; walls are dark & desaturated blue-grey
    return (L < 150) | (S > 0.30)

for tag, ps in [("r4 death", d4), ("r5 death", d5), ("r5 pogo", pg)]:
    for i, q in enumerate(ps):
        m = effect_mask(q)
        print(f"  {tag} p{i+1}: non-board area {100.0*m.sum()/m.size:5.2f}%")

print()
print("=== BUILDER CLAIM: vertical profile 221 at top edge -> 6 at centre-bottom ===")
# find the effect blob in panel 2 of r5 death
q = d5[1]
L, S = lum(q), sat(q)
blob = (L < 120) | (S > 0.35)
blob = binary_opening(blob, np.ones((3, 3)))
lab, n = label(blob)
sizes = np.bincount(lab.ravel()); sizes[0] = 0
big = int(sizes.argmax())
ys, xs = np.where(lab == big)
print(f"  largest effect blob: {sizes[big]} px, bbox y[{ys.min()}..{ys.max()}] x[{xs.min()}..{xs.max()}]")
print(f"  bbox size {xs.max()-xs.min()+1} x {ys.max()-ys.min()+1} px "
      f"(cell ~109px on a 512 panel -> {(xs.max()-xs.min()+1)/109:.2f} x {(ys.max()-ys.min()+1)/109:.2f} cells)")
cx = int(xs.mean())
print("  column profile through centroid x=%d (mean L over +/-8px):" % cx)
for y in range(ys.min(), ys.max()+1, max(1, (ys.max()-ys.min())//14)):
    band = L[y, max(0,cx-8):cx+8]
    print(f"    y={y:3d} meanL={band.mean():6.1f} minL={band.min():6.1f} maxL={band.max():6.1f}")

print()
print("=== interior local contrast (9x9 max-min) inside the effect blob ===")
for tag, ps in [("r4 death", d4), ("r5 death", d5)]:
    q = ps[1]
    L = lum(q)
    lc = maximum_filter(L, 9) - minimum_filter(L, 9)
    b = (L < 120) | (sat(q) > 0.35)
    b = binary_opening(b, np.ones((5, 5)))
    if b.sum() > 100:
        print(f"  {tag}: interior mean local contrast {lc[b].mean():.2f} over {b.sum()} px")
