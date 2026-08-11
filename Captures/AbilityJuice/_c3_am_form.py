"""Step 4 - hierarchy, internal spread, occlusion (seams), and the frozen tail."""
import numpy as np, glob, os
from PIL import Image
from scipy import ndimage

PC = 218.0; FPS = 30.0; T0 = -0.40
DIR = "Captures/AbilityJuice/shots/r2/aftermath"
FR = sorted(glob.glob(os.path.join(DIR, "f*.jpg")))
def load(i): return np.asarray(Image.open(FR[i]).convert("RGB")).astype(np.float64)
def lum(a):  return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def tt(i):   return T0 + i/FPS
A0 = load(0); L0 = lum(A0)

# ---------------- the frozen tail: is anything changing at all? -------------
print("=== TAIL: per-frame pixel delta between consecutive frames ===")
prev = None
for i in range(40, 72):
    L = lum(load(i))
    if prev is not None:
        d = np.abs(L - prev)
        chg = (d > 3).sum()
        print(f"  t={tt(i):+.2f}  changed px(>3) = {chg:7d}   maxdelta = {d.max():6.1f}   "
              f"meandelta = {d.mean():.3f}")
    prev = L

# ---------------- 1. HIERARCHY at the strip frames -------------------------
print("\n=== 1. MASS HIERARCHY (connected components of occluding material) ===")
for i in (25, 29, 34):
    A = load(i); L = lum(A); d = L - L0
    dark = d < -40
    dark = ndimage.binary_opening(dark, np.ones((3, 3)))
    lab, n = ndimage.label(dark)
    sizes = ndimage.sum(dark, lab, range(1, n+1))
    order = np.argsort(-sizes)
    print(f"\n t={tt(i):+.2f}s  {n} components")
    objs = ndimage.find_objects(lab)
    widths = []
    for k in order[:10]:
        sl = objs[k]
        w = (sl[1].stop-sl[1].start)/PC; h = (sl[0].stop-sl[0].start)/PC
        a = sizes[k]/PC**2
        if a < 0.004: continue
        widths.append(max(w, h))
        print(f"   blob {len(widths):2d}: {w:.2f} x {h:.2f} cells   area {a:.3f} cells^2")
    if len(widths) >= 2:
        print(f"   -> dominant {widths[0]:.2f} cells; largest satellite {widths[1]:.2f}; "
              f"ratio {widths[0]/widths[1]:.2f}:1")

# ---------------- 2. INTERNAL LUMINANCE SPREAD inside the dominant lobe ----
print("\n=== 2. INTERNAL LUMINANCE SPREAD inside dominant lobe ===")
for i in (25, 29):
    A = load(i); L = lum(A); d = L - L0
    dark = ndimage.binary_opening(d < -40, np.ones((3, 3)))
    lab, n = ndimage.label(dark)
    sizes = ndimage.sum(dark, lab, range(1, n+1))
    k = int(np.argmax(sizes)) + 1
    m = lab == k
    # erode 6px so edge gradient does not inflate the spread
    core = ndimage.binary_erosion(m, np.ones((13, 13)))
    if core.sum() < 500: core = m
    v = L[core]
    print(f" t={tt(i):+.2f}s  lobe px={core.sum()}  mean {v.mean():6.1f}  std {v.std():5.1f}  "
          f"p5 {np.percentile(v,5):5.1f}  p95 {np.percentile(v,95):6.1f}  "
          f"p95-p5 = {np.percentile(v,95)-np.percentile(v,5):5.1f}  min {v.min():.1f} max {v.max():.1f}")

# ---------------- 3. OCCLUSION: tile seam contrast -------------------------
print("\n=== 3. OCCLUSION: tile seam contrast, clean vs under thickest smoke ===")
# Seams are dark lines every 218px. Find seam columns on the clean plate.
prof = L0[int(1024*0.72):int(1024*0.80), :].mean(axis=0)
seam_cols = []
for x in range(6, 1018):
    w = prof[x-5:x+6]
    if prof[x] == w.min() and (w.max()-w.min()) > 8:
        if not seam_cols or x - seam_cols[-1] > 60:
            seam_cols.append(x)
print(" seam columns detected:", seam_cols)

def seam_contrast(L, x, y0, y1):
    """peak-to-trough across a vertical seam at column x, averaged over rows y0..y1"""
    strip = L[y0:y1, x-12:x+13].mean(axis=0)
    return float(strip.max() - strip.min())

# pick the seam+rows sitting under the densest part of the plume
i = 29
A = load(i); L = lum(A); d = L - L0
dense = ndimage.binary_erosion(d < -90, np.ones((9, 9)))
ys, xs = np.nonzero(dense)
print(f" densest-material mask at t={tt(i):+.2f}: {dense.sum()} px, "
      f"rows {ys.min()}..{ys.max()}, cols {xs.min()}..{xs.max()}")
for x in seam_cols:
    sel = dense[:, max(0,x-12):x+13].any(axis=1)
    rows = np.nonzero(sel)[0]
    if len(rows) < 25: continue
    y0, y1 = rows[0], rows[-1]
    c_clean = seam_contrast(L0, x, y0, y1)
    c_smoke = seam_contrast(L,  x, y0, y1)
    print(f"  seam x={x} rows {y0}..{y1}:  clean {c_clean:5.1f}  ->  under smoke {c_smoke:5.1f}"
          f"   ({100*c_smoke/max(c_clean,1e-6):.0f}%残)")
