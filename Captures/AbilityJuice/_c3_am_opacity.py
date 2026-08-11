"""Step 9 - decisive opacity test: does the floor structure still CORRELATE with
what we see under the smoke? Opaque material => correlation collapses."""
import numpy as np, glob, os
from PIL import Image
from scipy import ndimage

PC = 218.0; T0 = -0.40
DIR = "Captures/AbilityJuice/shots/r2/aftermath"
FR = sorted(glob.glob(os.path.join(DIR, "f*.jpg")))
def load(i): return np.asarray(Image.open(FR[i]).convert("RGB")).astype(np.float64)
def lum(a):  return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def tt(i):   return T0+i/30.0
A0 = load(0); L0 = lum(A0)

def hipass(X, s=9):
    return X - ndimage.uniform_filter(X, s)

print("=== opacity by structural correlation (high-pass floor vs high-pass image) ===")
print("corr ~1.0 = fully transparent, ~0.0 = fully opaque; slope = transmittance")
for i in (25, 29, 33, 38, 42, 52):
    A = load(i); L = lum(A)
    hp0 = hipass(L0); hp = hipass(L)
    for lo, hi, nm in ((0, 45, "deep smoke  L<45"),
                       (45, 90, "mid smoke   45-90"),
                       (90, 140, "thin smoke  90-140")):
        m = (L >= lo) & (L < hi) & (np.abs(hp0) > 4)
        m = ndimage.binary_erosion(m, np.ones((3,3)))
        if m.sum() < 400:
            continue
        a, b = hp0[m], hp[m]
        c = float(np.corrcoef(a, b)[0,1])
        slope = float(np.polyfit(a, b, 1)[0])
        print(f" t={tt(i):+.2f} {nm}: n={m.sum():6d}  corr {c:+.3f}  transmittance {slope:+.3f} "
              f"-> opacity {100*(1-max(slope,0)):.0f}%")
    print()

print("=== same test on the GROUND MARK (is the scorch opaque or a tint?) ===")
for i in (46, 52, 58, 62):
    A = load(i); L = lum(A); d = L - L0
    m = (d < -35) & (np.abs(hipass(L0)) > 4)
    m = ndimage.binary_erosion(m, np.ones((3,3)))
    if m.sum() < 300:
        print(f" t={tt(i):+.2f}: n={m.sum()}"); continue
    a, b = hipass(L0)[m], hipass(L)[m]
    print(f" t={tt(i):+.2f}: n={m.sum():6d}  corr {np.corrcoef(a,b)[0,1]:+.3f}  "
          f"transmittance {np.polyfit(a,b,1)[0]:+.3f}  mean delta {d[m].mean():.1f}")

print("\n=== ground-mark edge: hard step or gradient? ===")
i = 52
A = load(i); L = lum(A); d = L - L0
m = ndimage.binary_fill_holes(d < -25)
lab, n = ndimage.label(ndimage.binary_opening(m, np.ones((5,5))))
sizes = ndimage.sum(m, lab, range(1, n+1)); k = int(np.argmax(sizes))+1
mm = lab == k
ys, xs = np.nonzero(mm); cy, cx = int(ys.mean()), int(xs.mean())
print(f" mark centre ({cy},{cx}) size {mm.sum()/PC**2:.2f} cells^2")
for ang in range(0, 360, 45):
    dy, dx = np.sin(np.radians(ang)), np.cos(np.radians(ang))
    prof = []
    for r in range(0, 260):
        y, x = int(cy+dy*r), int(cx+dx*r)
        if not (0 <= y < 1024 and 0 <= x < 1024): break
        prof.append(d[y, x])
    prof = np.array(prof)
    inside = prof[:20].mean()
    idx = np.nonzero(prof > inside*0.10)[0]
    if len(idx) == 0: continue
    edge = idx[0]
    # 90%->10% transition width
    hi_i = np.nonzero(prof < inside*0.90)[0]
    hi = hi_i[0] if len(hi_i) else edge
    print(f"  ray {ang:3d} deg: mark radius {edge/PC:.2f} cells, "
          f"edge 90%->10% over {edge-hi:3d} px ({(edge-hi)/PC:.3f} cells), "
          f"slope {abs(inside)*0.8/max(edge-hi,1):.1f} L/px")
# irregularity of the outline
rs = []
for ang in range(0, 360, 10):
    dy, dx = np.sin(np.radians(ang)), np.cos(np.radians(ang))
    r = 0
    while r < 300:
        y, x = int(cy+dy*r), int(cx+dx*r)
        if not (0 <= y < 1024 and 0 <= x < 1024) or not mm[y, x]: break
        r += 1
    rs.append(r/PC)
rs = np.array(rs)
print(f" outline radius mean {rs.mean():.2f} std {rs.std():.3f} cells "
      f"(cv {rs.std()/rs.mean():.2f}; a circle=0.00, a square~0.11)")
