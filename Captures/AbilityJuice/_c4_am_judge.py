"""Round-3 aftermath verification. Every number measured from the captured frames."""
import numpy as np, glob, os, json
from PIL import Image
from scipy import ndimage

PC = 218.0                       # px per 2.7-unit cell in the 1024 capture
FPS = 30.0
T0 = -0.40
DIR = "Captures/AbilityJuice/shots/r3/aftermath"
FR = sorted(glob.glob(os.path.join(DIR, "f*.jpg")))
N = len(FR)
PICK = dict(windup=25, impact=29, after=42)

def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)
def frame(i): return load(FR[i])
def lum(a): return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def tt(i): return T0 + i/FPS
def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0)
def hue(a):
    r, g, b = a[...,0], a[...,1], a[...,2]
    mx = a.max(axis=-1); mn = a.min(axis=-1); d = mx-mn
    h = np.zeros_like(mx); m = d > 1e-6
    im = m & (mx == r); h[im] = ((g-b)[im]/d[im]) % 6
    im = m & (mx == g); h[im] = ((b-r)[im]/d[im]) + 2
    im = m & (mx == b); h[im] = ((r-g)[im]/d[im]) + 4
    return h*60.0

A0 = frame(0); L0 = lum(A0)
print(f"N frames {N}  t {tt(0):+.2f}..{tt(N-1):+.2f}")
print(f"clean floor median L = {np.percentile(L0,50):.1f}  "
      f"mean rgb {A0.reshape(-1,3).mean(0).round(1)}")

# ---------------------------------------------------------------- 1. HOT+SAT
print("\n=== 1. bright(L>200) AND saturated(>0.45) — the round-2 rejection ===")
print(f"{'t':>7} {'px':>7} {'cells2':>7} {'%frame':>7} {'clip255':>7} {'maxSatHot':>9}")
hot_rows = []
for i in range(N):
    A = frame(i); L = lum(A); S = sat(A)
    m = (L > 200) & (S > 0.45)
    n = int(m.sum())
    clip = int((A.max(axis=-1) >= 254).sum())
    ms = float(S[L > 200].max()) if (L > 200).any() else 0.0
    hot_rows.append((i, tt(i), n, n/PC**2, 100*n/L.size, clip, ms))
    if n > 0 or i in PICK.values():
        print(f"{tt(i):+7.2f} {n:7d} {n/PC**2:7.3f} {100*n/L.size:7.3f} {clip:7d} {ms:9.3f}")
best = max(hot_rows, key=lambda r: r[2])
print(f"\nPEAK hot+sat: t={best[1]:+.2f} (frame {best[0]}) = {best[2]} px "
      f"= {best[3]:.3f} cells2 = {best[4]:.2f}% of frame")
for k, i in PICK.items():
    r = hot_rows[i]
    print(f"  strip panel {k:7s} f{i} t={r[1]:+.2f}: {r[2]} px = {r[3]:.3f} cells2 = {r[4]:.2f}%")

# ------------------------------------------------------- 2. EMBER COLOUR
print("\n=== 2. ember colour on the panels ===")
for k, i in PICK.items():
    A = frame(i); L = lum(A); S = sat(A); H = hue(A)
    m = (L > 200) & (S > 0.45)
    if m.sum() > 20:
        px = A[m]
        print(f"{k:7s} f{i}: hot+sat mean rgb {px.mean(0).round(1)} "
              f"median {np.median(px,0).round(1)} sat {S[m].mean():.3f} hue {np.median(H[m]):.1f}")
    # brightest 200 pixels overall
    idx = np.argsort(L.ravel())[-200:]
    px = A.reshape(-1,3)[idx]
    print(f"        top200-L mean rgb {px.mean(0).round(1)} sat {sat(px).mean():.3f} "
          f"hue {np.median(hue(px)):.1f}  maxL {L.max():.1f}")
    # warm mask: anything appreciably warmer than the cool board
    warm = (A[...,0] - A[...,2] > 30)
    if warm.sum() > 50:
        px = A[warm]
        print(f"        warm(R-B>30) n={int(warm.sum())} ({warm.sum()/PC**2:.2f} cells2) "
              f"mean rgb {px.mean(0).round(1)} sat {sat(px).mean():.3f} "
              f"medL {np.median(lum(px)):.1f}")

# --------------------------------------------------- 3. WHITE CORE SIZE
print("\n=== 3. white core (L>235 and sat<0.20) ===")
for i in range(N):
    A = frame(i); L = lum(A); S = sat(A)
    m = (L > 235) & (S < 0.20)
    if m.sum() > 0:
        print(f"  t={tt(i):+.2f} {int(m.sum()):6d} px = {m.sum()/PC**2:.4f} cells2")

# ------------------------------------------- 4. GROUND MARK MULTIPLIER
print("\n=== 4. ground mark per-channel multiplier vs clean floor ===")
print(f"{'t':>7} {'area_c2':>8} {'multR':>6} {'multG':>6} {'multB':>6} {'rgb':>20} {'sat':>6}")
for i in range(N):
    A = frame(i); L = lum(A); d = L - L0
    # ground mark = darkened floor, excluding the airborne smoke (which is much darker)
    m = (d < -18) & (d > -95)
    if m.sum() < 800: continue
    lab = ndimage.label(m)[0]
    if lab.max() == 0: continue
    sizes = ndimage.sum(m, lab, range(1, lab.max()+1))
    keep = lab == (np.argmax(sizes)+1)
    if keep.sum() < 800: continue
    mult = A[keep].mean(0) / A0[keep].mean(0)
    if i % 3 == 0 or i in (36, 42, 48):
        print(f"{tt(i):+7.2f} {keep.sum()/PC**2:8.3f} {mult[0]:6.3f} {mult[1]:6.3f} {mult[2]:6.3f} "
              f"{str(A[keep].mean(0).round(1)):>20} {sat(A[keep]).mean():6.3f}")

# --------------------------------------------------- 5. SATELLITE LADDER
print("\n=== 5. satellite ladder (dark blobs, d<-40) on panel frames ===")
for k, i in PICK.items():
    A = frame(i); d = lum(A) - L0
    m = d < -40
    lab, n = ndimage.label(m)
    sizes = ndimage.sum(m, lab, range(1, n+1))
    objs = ndimage.find_objects(lab)
    diam = []
    for j, s in enumerate(sizes):
        if s < 60: continue
        sl = objs[j]
        w = (sl[1].stop-sl[1].start)/PC; h = (sl[0].stop-sl[0].start)/PC
        diam.append(max(w, h))
    diam.sort(reverse=True)
    print(f"{k:7s} f{i}: {len(diam)} blobs  " + " / ".join(f"{x:.2f}" for x in diam[:10]))

# -------------------------------------------------------- 6. TAIL MOTION
print("\n=== 6. tail: is anything still changing +1.00 .. +1.70s ===")
prev = None
print(f"{'t':>7} {'anyA_c2':>8} {'minL':>6} {'bboxW':>6} {'bboxH':>6} {'cx':>7} {'cy':>7} {'dMAE':>7} {'identical':>9}")
for i in range(N):
    t = tt(i)
    if not (0.95 <= t <= 1.80): continue
    A = frame(i); L = lum(A); d = L - L0
    m = np.abs(d) > 8
    row = [f"{t:+7.2f}", f"{m.sum()/PC**2:8.3f}"]
    if m.sum() > 200:
        ys, xs = np.nonzero(m); w = np.clip(np.abs(d[m]), 0, None)
        row += [f"{L[m].min():6.1f}", f"{(xs.max()-xs.min()+1)/PC:6.2f}",
                f"{(ys.max()-ys.min()+1)/PC:6.2f}",
                f"{(xs*w).sum()/w.sum()/PC:7.3f}", f"{(ys*w).sum()/w.sum()/PC:7.3f}"]
    else:
        row += ["     --"]*5
    if prev is not None:
        mae = np.abs(A - prev).mean()
        row += [f"{mae:7.4f}", f"{'YES' if mae == 0 else '':>9}"]
    else:
        row += ["     --", "         "]
    prev = A
    print(" ".join(row))

# ------------------------------------------- 7. REFERENCE COMPARISON
print("\n=== 7. Clash Mini reference: right panel hot+sat %, floor L ===")
for p in sorted(glob.glob("Captures/AbilityJuice/strips/reference/*.jpg")):
    im = load(p)
    pan = im[:, -512:, :]
    L = lum(pan); S = sat(pan)
    m = (L > 200) & (S > 0.45)
    print(f"  {os.path.basename(p):28s} hot+sat {100*m.sum()/L.size:6.2f}%  "
          f"medL {np.median(L):5.1f}  medSat {np.median(S):.3f}  maxL {L.max():.0f}")

print("\n=== 7b. our strip panels measured the same way (512x512 panel) ===")
for tag, path in [("r3", "Captures/AbilityJuice/strips/r3/aftermath.jpg"),
                  ("r2", "Captures/AbilityJuice/strips/r2/aftermath.jpg")]:
    im = load(path)
    for pi, x0 in enumerate([0, 520, 1040]):
        pan = im[:, x0:x0+512, :]
        L = lum(pan); S = sat(pan)
        m = (L > 200) & (S > 0.45)
        print(f"  {tag} panel{pi+1}: hot+sat {100*m.sum()/L.size:6.2f}%  medL {np.median(L):5.1f}  "
              f"maxL {L.max():.0f}  darkfrac(L<110) {100*(L<110).mean():5.2f}%")
