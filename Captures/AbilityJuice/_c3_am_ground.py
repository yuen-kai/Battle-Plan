"""Step 5 - per-row seam occlusion, the ground mark, and the embers."""
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
def hue(a):
    r, g, b = a[...,0], a[...,1], a[...,2]
    mx = a.max(axis=-1); mn = a.min(axis=-1); d = mx-mn
    h = np.zeros_like(mx); m = d > 1e-6
    im = m & (mx == r); h[im] = ((g-b)[im]/d[im]) % 6
    im = m & (mx == g); h[im] = ((b-r)[im]/d[im]) + 2
    im = m & (mx == b); h[im] = ((r-g)[im]/d[im]) + 4
    return h*60.0

A0 = load(0); L0 = lum(A0)
FLOOR = float(np.percentile(L0, 50))

# ================= 3. OCCLUSION, per-row, only where fully covered =========
print("=== 3. OCCLUSION: per-row seam contrast where the seam is fully buried ===")
prof = L0[int(1024*0.72):int(1024*0.80), :].mean(axis=0)
seam_cols = [185, 405, 618, 838]
for i in (25, 29):
    A = load(i); L = lum(A); d = L - L0
    for x in seam_cols:
        results = []
        for y in range(20, 1000):
            win_clean = L0[y, x-11:x+12]
            c_clean = win_clean.max() - win_clean.min()
            if c_clean < 25:   # only rows where the seam is genuinely visible when clean
                continue
            # require the whole 23px window to be covered by strong material
            if not (d[y, x-11:x+12] < -70).all():
                continue
            win = L[y, x-11:x+12]
            results.append((c_clean, win.max()-win.min()))
        if len(results) < 10:
            print(f" t={tt(i):+.2f} seam x={x}: only {len(results)} fully-buried rows")
            continue
        r = np.array(results)
        print(f" t={tt(i):+.2f} seam x={x}: {len(r):4d} buried rows | clean {r[:,0].mean():5.1f} "
              f"-> buried {r[:,1].mean():5.1f} (median {np.median(r[:,1]):5.1f}, "
              f"p90 {np.percentile(r[:,1],90):5.1f})")

# ================= 5. GROUND MARK ==========================================
print("\n=== 5. GROUND MARK: signed luminance vs clean floor ===")
print(f"{'t':>7} {'darkestDelta':>13} {'p05Delta':>9} {'meanNegDelta':>13} {'area(cells^2)':>13} {'brightDelta':>12}")
for i in range(12, 78, 2):
    A = load(i); L = lum(A); d = L - L0
    neg = d < -25
    if neg.sum() < 200:
        print(f"{tt(i):+7.2f}  (no dark ground signal)")
        continue
    # ground-only band: rows below the plume's rising body -> use lower 45% of the neg mask
    ys = np.nonzero(neg)[0]
    cut = np.percentile(ys, 55)
    g = neg.copy(); g[:int(cut), :] = False
    dd = d[g]
    print(f"{tt(i):+7.2f} {dd.min():13.1f} {np.percentile(dd,5):9.1f} {dd.mean():13.1f} "
          f"{g.sum()/PC**2:13.2f} {d.max():12.1f}")

# late residual only (after plume gone)
print("\n-- residual mark once the plume has cleared --")
for i in (60, 64, 68, 72):
    A = load(i); L = lum(A); d = L - L0
    neg = d < -10
    if neg.sum() < 100: 
        print(f" t={tt(i):+.2f}: nothing >10 below floor"); continue
    lab, n = ndimage.label(ndimage.binary_opening(neg, np.ones((5,5))))
    if n == 0:
        print(f" t={tt(i):+.2f}: nothing"); continue
    sizes = ndimage.sum(neg, lab, range(1, n+1)); k = int(np.argmax(sizes))+1
    m = lab == k
    sl = ndimage.find_objects(lab)[k-1]
    dd = d[m]
    print(f" t={tt(i):+.2f}: mark {(sl[1].stop-sl[1].start)/PC:.2f} x "
          f"{(sl[0].stop-sl[0].start)/PC:.2f} cells, mean delta {dd.mean():.1f}, "
          f"darkest {dd.min():.1f}, area {m.sum()/PC**2:.2f} cells^2")

# ================= 6. EMBERS ===============================================
print("\n=== 6. EMBERS: warm-pixel census ===")
print(f"{'t':>7} {'warmPx':>7} {'maxL':>6} {'meanSat':>8} {'maxSat':>7} {'meanHue':>8} "
      f"{'L/floor':>8} {'blobs':>6} {'sizeRange(cells)':>18}")
for i in range(12, 66, 2):
    A = load(i); H = hue(A); S = sat(A); L = lum(A)
    warm = (S >= 0.45) & (((H >= 5) & (H <= 55))) & (L > 60)
    n = int(warm.sum())
    if n < 20:
        print(f"{tt(i):+7.2f} {n:7d}   --")
        continue
    lab, nb = ndimage.label(ndimage.binary_opening(warm, np.ones((3,3))))
    ws = []
    if nb:
        for sl in ndimage.find_objects(lab):
            ws.append(max(sl[1].stop-sl[1].start, sl[0].stop-sl[0].start)/PC)
        ws.sort()
    print(f"{tt(i):+7.2f} {n:7d} {L[warm].max():6.1f} {S[warm].mean():8.2f} {S[warm].max():7.2f} "
          f"{H[warm].mean():8.1f} {L[warm].max()/FLOOR:8.2f} {nb:6d} "
          f"{(f'{ws[0]:.2f}..{ws[-1]:.2f}' if ws else '-'):>18}")
