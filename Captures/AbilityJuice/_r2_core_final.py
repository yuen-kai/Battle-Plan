"""Final calibration: Clash disc size + light spill, our white-core hold, seam occlusion."""

import os

import numpy as np
from PIL import Image
from scipy import ndimage

REF = "Captures/AbilityJuice/strips/reference"
RAW = "Captures/AbilityJuice/shots/r2/impactcore"
PPC = 217.7


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


print("=== CLASH: white disc as a fraction of panel width, and board lift outside it ===")
for n in ("cm-everyability-23", "cm-everyability-20", "cm-everyability-30", "cm-everyability-05"):
    im = Image.open(f"{REF}/{n}.jpg").convert("RGB")
    pw = im.width // 3
    p1 = np.asarray(im.crop((6, 0, pw - 6, im.height))).astype(np.float32)
    p2 = np.asarray(im.crop((pw + 6, 0, 2 * pw - 6, im.height))).astype(np.float32)
    clip = (p2 >= 250).all(axis=-1)
    lab, k = ndimage.label(clip)
    if k:
        sizes = ndimage.sum(clip, lab, range(1, k + 1))
        big = lab == (np.argmax(sizes) + 1)
        ys, xs = np.nonzero(big)
        eqd = 2 * np.sqrt(big.sum() / np.pi)
        # board lift: same 40px corner patch, panel1 -> panel2
        c1 = lum(p1[0:60, 0:60]).mean()
        c2 = lum(p2[0:60, 0:60]).mean()
        # ring of floor just outside the disc
        cy, cx = ys.mean(), xs.mean()
        Y, X = np.mgrid[0:p2.shape[0], 0:p2.shape[1]]
        R = np.sqrt((X - cx) ** 2 + (Y - cy) ** 2)
        ring = (R > eqd * 0.62) & (R < eqd * 0.95)
        print(f"{n:22s} disc eqdiam {eqd:5.0f}px = {100*eqd/p2.shape[1]:4.1f}% of panel width; "
              f"floor ring just outside disc mean lum {lum(p2)[ring].mean():5.0f} "
              f"vs panel-1 same ring {lum(p1)[ring].mean():5.0f} ({lum(p2)[ring].mean()-lum(p1)[ring].mean():+5.0f}); "
              f"far corner {c1:.0f}->{c2:.0f} ({c2-c1:+.0f})")

print("\n=== OURS: white core over time (30fps, t=0 at first lit frame f0012) ===")


def load(i):
    return np.asarray(Image.open(os.path.join(RAW, f"f{i:04d}.jpg")).convert("RGB")).astype(np.float32)


for i in range(12, 18):
    a = load(i)
    w = (a >= 254).all(axis=-1)
    lab, k = ndimage.label(w)
    if k:
        sizes = ndimage.sum(w, lab, range(1, k + 1))
        big = w & (lab == (np.argmax(sizes) + 1))
    else:
        big = w
    eqd = 2 * np.sqrt(max(big.sum(), 1) / np.pi)
    print(f"  t={(i-12)*33.3:5.1f}ms  white eqdiam {eqd:5.0f}px = {eqd/PPC:.2f} cells "
          f"= {100*eqd/1024:4.1f}% of panel width   clipped {100*w.mean():5.2f}% of panel")

print("\n=== OURS: seam occlusion test (findings demand seam contrast < 8 under opaque) ===")
clean = load(0)
cl = lum(clean)
a13 = lum(load(13))
a12 = lum(load(12))
# find strong vertical seams in the clean frame near the effect
for y in (560, 600, 640):
    rowc = cl[y]
    for x in range(300, 740):
        if rowc[x] < rowc[x - 12] - 30 and rowc[x] < rowc[x + 12] - 30:
            c_clean = (rowc[x - 12] + rowc[x + 12]) / 2 - rowc[x]
            c13 = (a13[y, x - 12] + a13[y, x + 12]) / 2 - a13[y, x]
            c12 = (a12[y, x - 12] + a12[y, x + 12]) / 2 - a12[y, x]
            r = np.hypot(x - 512, y - 493) / PPC
            print(f"  seam at ({x},{y}) r={r:.2f}c: contrast clean {c_clean:5.1f} -> "
                  f"f0012 {c12:5.1f} -> f0013 {c13:5.1f}")
            break
