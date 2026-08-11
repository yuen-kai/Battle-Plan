"""Step 13 - is the internal spread real smoke shading, or the ember streaks?
Plus a global saturation sanity check."""
import numpy as np, glob
from PIL import Image
from scipy import ndimage

PC = 218.0
FR = sorted(glob.glob("Captures/AbilityJuice/shots/r2/aftermath/f*.jpg"))
def load(i): return np.asarray(Image.open(FR[i]).convert("RGB")).astype(np.float64)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(-1); mn=a.min(-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1),0)
L0 = lum(load(0))

print("=== internal spread of the SMOKE ONLY (ember pixels removed) ===")
for i in (25, 29, 33):
    A = load(i); L = lum(A)
    RmB = A[...,0]-A[...,2]
    dark = ndimage.binary_opening(L - L0 < -40, np.ones((3,3)))
    lab, n = ndimage.label(dark)
    k = int(np.argmax(ndimage.sum(dark, lab, range(1, n+1))))+1
    lobe = ndimage.binary_erosion(lab == k, np.ones((13,13)))
    smoke = lobe & (RmB < 18) & ~ndimage.binary_dilation(RmB > 25, np.ones((9,9)))
    for nm, m in (("lobe incl. embers", lobe), ("smoke only", smoke)):
        if m.sum() < 500: continue
        v = L[m]
        print(f" t={-0.4+i/30:+.2f} {nm:18s}: n={m.sum():6d} mean {v.mean():6.1f} std {v.std():5.1f} "
              f"p5 {np.percentile(v,5):5.1f} p95 {np.percentile(v,95):6.1f} "
              f"spread {np.percentile(v,95)-np.percentile(v,5):5.1f}")
    print()

print("=== global saturation sanity: is ANY sizeable region above 0.45? ===")
for i in (25, 29, 33, 38):
    A = load(i); S = sat(A); L = lum(A)
    m = ndimage.binary_opening(S > 0.45, np.ones((3,3)))
    lab, n = ndimage.label(m)
    if n == 0:
        print(f" t={-0.4+i/30:+.2f}: no sat>0.45 region survives a 3x3 opening"); continue
    sz = ndimage.sum(m, lab, range(1, n+1))
    big = sz.max()
    k = int(np.argmax(sz))+1
    print(f" t={-0.4+i/30:+.2f}: {n} regions, largest {big:.0f}px ({big/PC**2:.4f} cells^2), "
          f"its mean L {L[lab==k].mean():.0f}, total sat>0.45 area {m.sum()/PC**2:.3f} cells^2")

print("\n=== round-1 vs round-2 side by side ===")
R1 = sorted(glob.glob("Captures/AbilityJuice/shots/r1/aftermath/f*.jpg"))
L1_0 = lum(np.asarray(Image.open(R1[0]).convert("RGB")).astype(np.float64))
def best(frames, base):
    out = []
    for p in frames:
        a = np.asarray(Image.open(p).convert("RGB")).astype(np.float64)
        L = lum(a); d = L - base
        out.append(((d < -40).sum(), L.min(), d.min()))
    return out
r1 = best(R1, L1_0); r2 = best(FR, L0)
i1 = int(np.argmax([x[0] for x in r1])); i2 = int(np.argmax([x[0] for x in r2]))
print(f" r1 peak occluding area {r1[i1][0]/PC**2:.2f} cells^2, darkest pixel L={r1[i1][1]:.0f}, "
      f"deepest delta {r1[i1][2]:.0f}")
print(f" r2 peak occluding area {r2[i2][0]/PC**2:.2f} cells^2, darkest pixel L={r2[i2][1]:.0f}, "
      f"deepest delta {r2[i2][2]:.0f}")
