"""Regression check: did r3 keep r2's confirmed-good smoke form, spread, occlusion, motion?"""
import numpy as np, glob, os
from PIL import Image
from scipy import ndimage
PC = 218.0; FPS = 30.0; T0 = -0.40
def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)
def lum(a): return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0)

for tag in ("r2", "r3"):
    FR = sorted(glob.glob(f"Captures/AbilityJuice/shots/{tag}/aftermath/f*.jpg"))
    A0 = load(FR[0]); L0 = lum(A0)
    print(f"\n--- {tag} ---")
    ws=[];hs=[];cys=[];cxs=[];spreads=[];areas=[]
    for i, f in enumerate(FR):
        A = load(f); L = lum(A); d = L - L0
        m = d < -60
        if m.sum() < 400: continue
        ys, xs = np.nonzero(m); w = -d[m]
        ws.append((xs.max()-xs.min()+1)/PC); hs.append((ys.max()-ys.min()+1)/PC)
        cys.append((ys*w).sum()/w.sum()/PC); cxs.append((xs*w).sum()/w.sum()/PC)
        areas.append(m.sum()/PC**2)
        lab, n = ndimage.label(m)
        sizes = ndimage.sum(m, lab, range(1, n+1))
        big = lab == (np.argmax(sizes)+1)
        v = L[big]
        spreads.append(np.percentile(v,95)-np.percentile(v,5))
    print(f"  plume bbox W {min(ws):.2f}..{max(ws):.2f}  H {min(hs):.2f}..{max(hs):.2f} cells")
    print(f"  centroid travel  X {max(cxs)-min(cxs):.2f}  Y {max(cys)-min(cys):.2f} cells "
          f"(rise {cys[0]-min(cys):+.2f})")
    print(f"  peak dark area {max(areas):.2f} cells2   internal L spread median {np.median(spreads):.1f}")
    # occlusion: seam suppression fully inside the darkest mass
    ib = int(np.argmax(areas))
    A = load(FR[ib+ [i for i,f in enumerate(FR)][0]]) if False else None

# occlusion measured properly: seam step inside the plume core vs same tiles clean
for tag, fi in (("r2", 29), ("r3", 29)):
    FR = sorted(glob.glob(f"Captures/AbilityJuice/shots/{tag}/aftermath/f*.jpg"))
    A0 = load(FR[0]); A = load(FR[fi]); L0 = lum(A0); L = lum(A)
    core = ndimage.binary_erosion(lum(A)-L0 < -80, iterations=6)
    if core.sum() > 500:
        gy, gx = np.gradient(L); g = np.hypot(gx, gy)
        gy0, gx0 = np.gradient(L0); g0 = np.hypot(gx0, gy0)
        print(f"{tag} f{fi}: inside plume core ({core.sum()/PC**2:.2f} cells2) "
              f"seam gradient p99 {np.percentile(g[core],99):.1f} "
              f"(same pixels clean: {np.percentile(g0[core],99):.1f})")

# gold mass vs plume base alignment on f29
FR = sorted(glob.glob("Captures/AbilityJuice/shots/r3/aftermath/f*.jpg"))
A = load(FR[29]); A0 = load(FR[0]); L = lum(A); L0 = lum(A0); S = sat(A)
plume = (L-L0) < -60
gold = (S>0.45)&(L>150)&(A[...,0]-A[...,2]>60)
py, px_ = np.nonzero(plume); gy, gx_ = np.nonzero(gold)
print(f"\nf29 plume centroid ({px_.mean()/PC:.2f},{py.mean()/PC:.2f})  "
      f"gold centroid ({gx_.mean()/PC:.2f},{gy.mean()/PC:.2f})  "
      f"offset {np.hypot(px_.mean()-gx_.mean(), py.mean()-gy.mean())/PC:.2f} cells")
