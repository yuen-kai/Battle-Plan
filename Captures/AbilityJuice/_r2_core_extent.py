"""Where does the blue actually end? Corners, far field, and connected components."""

import os

import numpy as np
from PIL import Image
from scipy import ndimage

RAW = "Captures/AbilityJuice/shots/r2/impactcore"
PPC = 217.7


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def load(i):
    return np.asarray(Image.open(os.path.join(RAW, f"f{i:04d}.jpg")).convert("RGB")).astype(np.float32)


def sat(a):
    mx, mn = a.max(axis=-1), a.min(axis=-1)
    return np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


clean = load(0)
cl = lum(clean)

patches = {
    "top-left corner   ": (np.s_[0:80], np.s_[0:80]),
    "top-right corner  ": (np.s_[0:80], np.s_[944:1024]),
    "bottom-left corner": (np.s_[944:1024], np.s_[0:80]),
    "bottom-right      ": (np.s_[944:1024], np.s_[944:1024]),
    "mid-left edge     ": (np.s_[472:552], np.s_[0:80]),
    "mid-right edge    ": (np.s_[472:552], np.s_[944:1024]),
    "crate top-left    ": (np.s_[30:80], np.s_[110:200]),
}

for idx in (12, 13, 14):
    a = load(idx)
    L = lum(a)
    S = sat(a)
    print(f"\n=== f{idx:04d} far field (clean -> frame) ===")
    for name, (sy, sx) in patches.items():
        c = cl[sy, sx].mean()
        v = L[sy, sx].mean()
        print(f"  {name}  lum {c:6.1f} -> {v:6.1f}  ({v-c:+6.1f})   sat {sat(clean)[sy,sx].mean():.3f} -> {S[sy,sx].mean():.3f}")

    # full radial sweep to frame edge along +x from centre
    row = 493
    print("  radial sweep along y=493 (cells from centre -> lum, sat, RGB):")
    for dx in range(0, 512, 48):
        x = 512 + dx
        if x >= 1024:
            break
        px = a[row, x]
        print(f"    {dx/PPC:4.2f}c  lum {L[row,x]:6.1f} (clean {cl[row,x]:6.1f})  sat {S[row,x]:.3f}  "
              f"rgb {px[0]:3.0f},{px[1]:3.0f},{px[2]:3.0f}")

# connected components of the clipped set on f0013
print("\n=== f0013 clipped-set components ===")
a = load(13)
clip = (a >= 254).all(axis=-1)
lab, n = ndimage.label(clip)
sizes = ndimage.sum(clip, lab, range(1, n + 1))
order = np.argsort(sizes)[::-1]
print(f"{n} components; largest 10:")
for k in order[:10]:
    m = lab == (k + 1)
    ys, xs = np.nonzero(m)
    eqd = 2 * np.sqrt(m.sum() / np.pi)
    print(f"  {int(sizes[k]):6d}px  bbox {xs.max()-xs.min()+1:3d}x{ys.max()-ys.min()+1:3d}  "
          f"eqdiam {eqd/PPC:.2f}c  centre ({xs.mean():.0f},{ys.mean():.0f})")

print("\n=== f0012 clipped-set components ===")
a = load(12)
clip = (a >= 254).all(axis=-1)
lab, n = ndimage.label(clip)
sizes = ndimage.sum(clip, lab, range(1, n + 1))
order = np.argsort(sizes)[::-1]
print(f"{n} components; largest 6:")
for k in order[:6]:
    m = lab == (k + 1)
    ys, xs = np.nonzero(m)
    eqd = 2 * np.sqrt(m.sum() / np.pi)
    print(f"  {int(sizes[k]):6d}px  bbox {xs.max()-xs.min()+1:3d}x{ys.max()-ys.min()+1:3d}  "
          f"eqdiam {eqd/PPC:.2f}c  centre ({xs.mean():.0f},{ys.mean():.0f})")
