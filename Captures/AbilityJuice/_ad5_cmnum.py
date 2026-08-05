import sys, glob
import numpy as np
from PIL import Image
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import binary_opening, binary_closing, binary_dilation, label, distance_transform_edt

# cm-everyability-20 / -23 panel 3 have yellow damage numerals over an orange explosion
for name, box in [("cm-everyability-20", (2, (60, 260, 200, 340))),
                  ("cm-everyability-23", (2, (60, 300, 180, 340)))]:
    p = panels(load(f"{STRIPS}/reference/{name}.jpg"))[name and 2]
    H, S, L = hue(p), sat(p), lum(p)
    num = (H > 38) & (H < 75) & (S > 0.55) & (L > 175)
    num = binary_opening(num, np.ones((2, 2)))
    lab, n = label(binary_closing(num, np.ones((5, 5))))
    sizes = np.bincount(lab.ravel()); sizes[0] = 0
    keep = [i for i in range(1, n + 1) if sizes[i] > 60]
    print(f"\n=== {name} panel 3: {len(keep)} yellow numeral blobs ===")
    for i in keep[:6]:
        ys, xs = np.where(lab == i)
        bl = lab == i
        ring = binary_dilation(bl, np.ones((13, 13))) & ~binary_dilation(bl, np.ones((5, 5)))
        # how dark is the immediate halo?
        halo = binary_dilation(bl, np.ones((7, 7))) & ~bl
        print(f"  blob ({xs.mean():.0f},{ys.mean():.0f}) {xs.max()-xs.min()+1}x{ys.max()-ys.min()+1}px "
              f"fillL {L[bl].mean():.0f} S {S[bl].mean():.2f} | halo L {L[halo].mean():.0f} "
              f"minL {L[halo].min():.0f} darkfrac(L<90) {100.0*(L[halo]<90).mean():.0f}% | "
              f"ring L {L[ring].mean():.0f}")
        dt = distance_transform_edt(binary_dilation(bl, np.ones((9,9))) & (L < 100))
        if dt.max() > 0:
            print(f"       dark stroke half-width max {dt.max():.1f}px on a "
                  f"{xs.max()-xs.min()+1}px-wide glyph -> stroke/glyph {2*dt.max()/(xs.max()-xs.min()+1):.2f}")

print()
print("=== ours for comparison: grenade numerals, same measure ===")
import json
picks = {q["shot"]: q for q in json.load(open(f"{STRIPS}/r5/picks.json"))}
p = panels(load(f"{STRIPS}/r5/grenade.jpg"))[1]
H, S, L = hue(p), sat(p), lum(p)
num = (H > 38) & (H < 75) & (S > 0.55) & (L > 175)
num = binary_opening(num, np.ones((2, 2)))
lab, n = label(binary_closing(num, np.ones((5, 5))))
sizes = np.bincount(lab.ravel()); sizes[0] = 0
for i in [i for i in range(1, n+1) if sizes[i] > 60][:6]:
    bl = lab == i
    ys, xs = np.where(bl)
    halo = binary_dilation(bl, np.ones((7, 7))) & ~bl
    print(f"  blob ({xs.mean():.0f},{ys.mean():.0f}) {xs.max()-xs.min()+1}x{ys.max()-ys.min()+1}px "
          f"fillL {L[bl].mean():.0f} S {S[bl].mean():.2f} | halo L {L[halo].mean():.0f} "
          f"minL {L[halo].min():.0f} darkfrac(L<90) {100.0*(L[halo]<90).mean():.0f}%")
    dt = distance_transform_edt(binary_dilation(bl, np.ones((9,9))) & (L < 100))
    if dt.max() > 0:
        print(f"       dark stroke half-width max {dt.max():.1f}px on a "
              f"{xs.max()-xs.min()+1}px-wide glyph -> stroke/glyph {2*dt.max()/(xs.max()-xs.min()+1):.2f}")
