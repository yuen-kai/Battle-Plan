import numpy as np
from PIL import Image
from scipy import ndimage
import os

ROOT = "/Users/cykai/Battle-Plan/Captures/AbilityJuice/shots/r2"
PXCELL = {"impactdip": 213.0, "impactcore": 232.0}
CENTRE = {"impactdip": (507.0, 483.0), "impactcore": (532.0, 478.0)}

def load(shot, i):
    return np.asarray(Image.open(os.path.join(ROOT, shot, f"f{i:04d}.jpg")).convert("RGB"),
                      dtype=np.float64)
def lum(a):
    return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]

print("=" * 96)
print("P.  THE CENTRAL CLAIM: 'pulls the floor from ~182 to 120-140 at strength 1'")
print("    Clean board only: pixels that are floor in pre-roll AND not blue-washed now.")
print("    Reported as the 5th percentile (the deepest flank) and the median of the")
print("    dip's peak annulus (0.8-1.9 cells, where the shader says the peak lives).")
print("=" * 96)

for shot in ("impactdip", "impactcore"):
    med = np.median(np.stack([load(shot, i) for i in range(12)]), axis=0)
    L0 = lum(med)
    mx, mn = med.max(axis=2), med.min(axis=2)
    s0 = (mx-mn)/np.maximum(mx, 1e-6)
    floor0 = ndimage.binary_erosion((L0 > 150) & (s0 < 0.16), np.ones((7, 7)))
    cy, cx = CENTRE[shot]; pc = PXCELL[shot]
    yy, xx = np.mgrid[0:1024, 0:1024]
    rr = np.hypot(yy-cy, xx-cx)/pc
    peak = floor0 & (rr >= 0.8) & (rr < 1.9)
    print(f"\n  --- {shot} --- baseline peak-annulus floor L = {L0[peak].mean():.1f}")
    print(f"  {'frame':>6} {'t(s)':>6} | {'n clean':>8} {'min':>6} {'p5':>6} {'p25':>6} "
          f"{'median':>7} {'mean':>6} | {'drop vs base':>12}")
    for i in range(11, 20):
        F = load(shot, i); L = lum(F)
        mxf, mnf = F.max(axis=2), F.min(axis=2)
        sf = (mxf-mnf)/np.maximum(mxf, 1e-6)
        clean = peak & (sf < 0.20) & (np.abs(F[..., 2]-F[..., 0]) < 40)
        if clean.sum() < 400:
            print(f"  f{i:04d} {(i-12)*0.03333:+6.2f} | {clean.sum():8d}  (too little clean floor left)")
            continue
        v = L[clean]
        print(f"  f{i:04d} {(i-12)*0.03333:+6.2f} | {clean.sum():8d} {v.min():6.1f} "
              f"{np.percentile(v,5):6.1f} {np.percentile(v,25):6.1f} {np.median(v):7.1f} "
              f"{v.mean():6.1f} | {v.mean()-L0[peak].mean():+12.1f}")

print()
print("=" * 96)
print("Q.  ARRIVAL / DEPARTURE SHAPE  (flicker check). Frame-to-frame change in the")
print("    clean-floor luminance of the peak annulus, dip shot.")
print("=" * 96)
med = np.median(np.stack([load("impactdip", i) for i in range(12)]), axis=0)
L0 = lum(med); mx, mn = med.max(axis=2), med.min(axis=2)
floor0 = ndimage.binary_erosion((L0 > 150) & ((mx-mn)/np.maximum(mx,1e-6) < 0.16), np.ones((7,7)))
cy, cx = CENTRE["impactdip"]; pc = PXCELL["impactdip"]
yy, xx = np.mgrid[0:1024, 0:1024]
rr = np.hypot(yy-cy, xx-cx)/pc
# a fixed set of pixels that are clean floor in EVERY frame of the window: no wash, no unit
keep = floor0 & (rr >= 0.9) & (rr < 2.4)
for i in range(11, 20):
    F = load("impactdip", i)
    mxf, mnf = F.max(axis=2), F.min(axis=2)
    keep &= ((mxf-mnf)/np.maximum(mxf,1e-6) < 0.20)
print(f"  tracking {keep.sum()} pixels that stay clean board across the whole event")
prev = None
for i in range(10, 22):
    v = float(lum(load("impactdip", i))[keep].mean())
    d = "" if prev is None else f"   step={v-prev:+7.2f}"
    bar = "#" * int(max(0, (L0[keep].mean()-v)/2))
    print(f"  f{i:04d} t={(i-12)*0.03333:+.2f}s  L={v:6.1f}{d}   {bar}")
    prev = v
print(f"  pre-roll reference L={L0[keep].mean():.1f}")
