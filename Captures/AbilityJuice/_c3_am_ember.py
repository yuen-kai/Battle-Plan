"""Step 6 - honest ember census (loose warm definition), strict seam occlusion,
plume-only rise, and ground-mark edge quality."""
import numpy as np, glob, os
from PIL import Image
from scipy import ndimage

PC = 218.0; FPS = 30.0; T0 = -0.40
DIR = "Captures/AbilityJuice/shots/r2/aftermath"
FR = sorted(glob.glob(os.path.join(DIR, "f*.jpg")))
def load(i): return np.asarray(Image.open(FR[i]).convert("RGB")).astype(np.float64)
def lum(a):  return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def tt(i):   return T0 + i/FPS
def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx-mn)/np.maximum(mx, 1e-6), 0.0)
A0 = load(0); L0 = lum(A0); FLOOR = float(np.percentile(L0, 50))

# ---------- 6a. LOOSE warm census: R-B warm bias, no saturation gate -------
print("=== 6a. WARM PIXELS by red-blue bias (loose) ===")
print(f"{'t':>7} {'warmPx':>7} {'maxL_warm':>10} {'p99L':>7} {'meanL':>7} {'maxSat':>7} "
      f"{'peak/floor':>11} {'RmB max':>8}")
for i in range(12, 70, 2):
    A = load(i); L = lum(A); S = sat(A)
    RmB = A[...,0] - A[...,2]
    warm = RmB > 25
    n = int(warm.sum())
    if n < 20:
        print(f"{tt(i):+7.2f} {n:7d}  --"); continue
    print(f"{tt(i):+7.2f} {n:7d} {L[warm].max():10.1f} {np.percentile(L[warm],99):7.1f} "
          f"{L[warm].mean():7.1f} {S[warm].max():7.2f} {L[warm].max()/FLOOR:11.2f} {RmB.max():8.1f}")

# ---------- 6b. peak channel values anywhere in frame ----------------------
print("\n=== 6b. hottest pixel in frame (is ANYTHING clipping?) ===")
for i in (18, 22, 25, 29, 33, 38, 42, 46):
    A = load(i); L = lum(A)
    clip = ((A > 250).all(axis=-1)).sum()
    print(f" t={tt(i):+.2f}  maxR {A[...,0].max():.0f} maxG {A[...,1].max():.0f} "
          f"maxB {A[...,2].max():.0f}  maxL {L.max():.1f}  ({L.max()/FLOOR:.2f}x floor)  "
          f"white-clipped px {clip}")

# ---------- 6c. ember contrast against its LOCAL background ---------------
print("\n=== 6c. ember vs the smoke it sits on (local contrast) ===")
for i in (25, 29, 33, 38, 42):
    A = load(i); L = lum(A)
    RmB = A[...,0] - A[...,2]
    warm = RmB > 30
    if warm.sum() < 30:
        print(f" t={tt(i):+.2f}: {warm.sum()} warm px"); continue
    ring = ndimage.binary_dilation(warm, np.ones((15,15))) & ~ndimage.binary_dilation(warm, np.ones((5,5)))
    print(f" t={tt(i):+.2f}: warm {warm.sum():6d} px  meanL {L[warm].mean():6.1f} "
          f"maxL {L[warm].max():6.1f} | surround meanL {L[ring].mean():6.1f} "
          f"-> local ratio {L[warm].mean()/max(L[ring].mean(),1e-6):.2f}x")

# ---------- 3b. STRICT seam occlusion under near-black material ------------
print("\n=== 3b. STRICT: seam rows buried under material >150 below floor ===")
for i in (25, 29):
    A = load(i); L = lum(A); d = L - L0
    for x in (405, 618):
        res = []
        for y in range(20, 1000):
            w0 = L0[y, x-11:x+12]
            if w0.max()-w0.min() < 25: continue
            if not (d[y, x-11:x+12] < -150).all(): continue
            w = L[y, x-11:x+12]
            res.append((w0.max()-w0.min(), w.max()-w.min()))
        if len(res) < 5:
            print(f" t={tt(i):+.2f} x={x}: {len(res)} rows buried under near-black"); continue
        r = np.array(res)
        print(f" t={tt(i):+.2f} x={x}: {len(r):4d} rows | clean {r[:,0].mean():5.1f} -> "
              f"buried {r[:,1].mean():5.1f} (median {np.median(r[:,1]):4.1f})")

# ---------- 4b. PLUME-ONLY rise (exclude the ground scorch) ---------------
print("\n=== 4b. PLUME-ONLY vertical travel (top edge + body centroid) ===")
# ground scorch lives in the lower part and is broad+flat; the plume is the
# tall dark mass. Track the topmost dark row and the centroid of dark pixels
# that are ABOVE the scorch band established late in the shot.
Lg = lum(load(50)); dg = Lg - L0
scorch = dg < -25
ys = np.nonzero(scorch)[0]
scorch_top = ys.min() if len(ys) else 1024
print(f" scorch band begins at row {scorch_top} ({scorch_top/PC:.2f} cells)")
print(f"{'t':>7} {'topRow(cells)':>14} {'plumeCy':>9} {'plumeH':>8} {'plumeW':>8} {'area':>7}")
first = None
for i in range(13, 60, 2):
    A = load(i); L = lum(A); d = L - L0
    dark = d < -40
    dark[scorch_top:, :] = False    # plume only
    if dark.sum() < 400:
        print(f"{tt(i):+7.2f}   (no plume)"); continue
    ys, xs = np.nonzero(dark); wgt = -d[dark]
    cy = float((ys*wgt).sum()/wgt.sum())/PC
    print(f"{tt(i):+7.2f} {ys.min()/PC:14.3f} {cy:9.3f} "
          f"{(ys.max()-ys.min())/PC:8.2f} {(xs.max()-xs.min())/PC:8.2f} {dark.sum()/PC**2:7.2f}")
