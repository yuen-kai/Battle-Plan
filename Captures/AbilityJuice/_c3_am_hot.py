"""Step 7 - are the bright pixels the coloured ones? (the round-1 trap)
plus a correctly-specified seam occlusion test."""
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
    r,g,b = a[...,0],a[...,1],a[...,2]; mx=a.max(-1); mn=a.min(-1); d=mx-mn
    h=np.zeros_like(mx); m=d>1e-6
    im=m&(mx==r); h[im]=((g-b)[im]/d[im])%6
    im=m&(mx==g); h[im]=((b-r)[im]/d[im])+2
    im=m&(mx==b); h[im]=((r-g)[im]/d[im])+4
    return h*60.0
A0 = load(0); L0 = lum(A0); FLOOR = float(np.percentile(L0, 50))

print("=== 7a. JOINT: for the brightest pixels, what colour are they? ===")
print("(round-1 trap: bright pixels colourless, coloured pixels dim)")
for i in (22, 25, 29, 33, 38, 42):
    A = load(i); L = lum(A); S = sat(A); H = hue(A)
    for thr, name in ((240, "L>240"), (200, "L>200")):
        m = L > thr
        if m.sum() < 10:
            print(f" t={tt(i):+.2f} {name}: {m.sum()} px"); continue
        # restrict to pixels that changed (exclude naturally bright floor)
        ch = m & ((L - L0) > 20)
        if ch.sum() < 10: ch = m
        print(f" t={tt(i):+.2f} {name}: {ch.sum():6d} px | sat mean {S[ch].mean():.2f} "
              f"med {np.median(S[ch]):.2f} p90 {np.percentile(S[ch],90):.2f} | "
              f"hue med {np.median(H[ch]):5.1f} | area {ch.sum()/PC**2:.3f} cells^2")
    print()

print("=== 7b. the 'ember' population: bright AND saturated AND warm ===")
print(f"{'t':>7} {'px':>7} {'cells^2':>8} {'meanL':>7} {'maxL':>6} {'medSat':>7} {'medHue':>7} {'blobs':>6} {'sizes(cells)':>22}")
for i in range(14, 50, 2):
    A = load(i); L = lum(A); S = sat(A); H = hue(A)
    ember = (L > 190) & (S > 0.30) & (H < 60) & ((L - L0) > 15)
    n = int(ember.sum())
    if n < 30:
        print(f"{tt(i):+7.2f} {n:7d}  --"); continue
    lab, nb = ndimage.label(ndimage.binary_opening(ember, np.ones((3,3))))
    ws = sorted(max(sl[1].stop-sl[1].start, sl[0].stop-sl[0].start)/PC
                for sl in ndimage.find_objects(lab)) if nb else []
    s = f"{ws[0]:.2f}/{ws[len(ws)//2]:.2f}/{ws[-1]:.2f}" if ws else "-"
    print(f"{tt(i):+7.2f} {n:7d} {n/PC**2:8.3f} {L[ember].mean():7.1f} {L[ember].max():6.1f} "
          f"{np.median(S[ember]):7.2f} {np.median(H[ember]):7.1f} {nb:6d} {s:>22}")

print("\n=== 7c. ember spatial asymmetry (rosette test) ===")
for i in (25, 29, 33, 38):
    A = load(i); L = lum(A); S = sat(A); H = hue(A)
    ember = (L > 190) & (S > 0.30) & (H < 60) & ((L - L0) > 15)
    if ember.sum() < 50: continue
    ys, xs = np.nonzero(ember)
    cy, cx = ys.mean(), xs.mean()
    ang = np.degrees(np.arctan2(ys-cy, xs-cx)) % 360
    histo, _ = np.histogram(ang, bins=12, range=(0, 360))
    frac = histo / histo.sum()
    rad = np.hypot(ys-cy, xs-cx)/PC
    print(f" t={tt(i):+.2f}: angular bin fractions {np.round(frac,3).tolist()}")
    print(f"          max/min bin = {frac.max()/max(frac.min(),1e-9):.1f}x  "
          f"(rosette would be ~1.0x)  radius p50 {np.median(rad):.2f} p95 {np.percentile(rad,95):.2f} cells")

print("\n=== 3c. SEAM OCCLUSION, correctly specified (absolute darkness) ===")
for i in (25, 29):
    A = load(i); L = lum(A)
    for x in (405, 618):
        rows = []
        for y in range(20, 1000):
            w0 = L0[y, x-11:x+12]
            if w0.max()-w0.min() < 25: continue     # seam visible when clean
            w  = L[y,  x-11:x+12]
            rows.append((y, w0.max()-w0.min(), w.max()-w.min(), w.mean()))
        if not rows: continue
        r = np.array(rows)
        deep = r[r[:,3] < 45]        # window mean luminance under 45 = deep smoke
        mid  = r[(r[:,3] >= 45) & (r[:,3] < 90)]
        print(f" t={tt(i):+.2f} x={x}: clean contrast {r[:,1].mean():5.1f}")
        for nm, sub in (("deep (<45L)", deep), ("mid (45-90L)", mid)):
            if len(sub) < 5: 
                print(f"     {nm:14s}: {len(sub)} rows"); continue
            print(f"     {nm:14s}: {len(sub):4d} rows, residual contrast mean {sub[:,2].mean():5.1f} "
                  f"median {np.median(sub[:,2]):5.1f}  -> opacity ~{100*(1-np.median(sub[:,2])/r[:,1].mean()):.0f}%")
