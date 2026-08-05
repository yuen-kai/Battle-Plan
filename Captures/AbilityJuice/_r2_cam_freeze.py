"""Raw (uncompensated) consecutive-frame change: is the freeze distinguishable from a hitch?"""
import numpy as np
from PIL import Image
from scipy import ndimage
import os

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"


def go(rnd, lo, hi, label):
    D = os.path.join(AJ, "shots", rnd, "camera")

    def L(i):
        return np.asarray(Image.open(os.path.join(D, f"f{i:04d}.jpg")).convert("L"),
                          dtype=np.float64)

    def C(i):
        return np.asarray(Image.open(os.path.join(D, f"f{i:04d}.jpg")).convert("RGB"),
                          dtype=np.float64)

    print(f"\n{'='*112}")
    print(f"{label} — RAW consecutive written frames, no motion compensation")
    print("(JPEG re-encode noise between two identical renders would sit near 0.3-0.5)")
    print('=' * 112)
    print(f"{'pair':>16} {'t(ms)':>8} | {'mean|Δ|':>8} {'px>8':>8} {'px>25':>8} {'max':>6} | "
          f"{'warm px>8':>10} {'read'}")
    for i in range(lo, hi):
        a, b = L(i), L(i + 1)
        d = np.abs(b - a)
        ca, cb = C(i), C(i + 1)
        warm = (np.abs(cb - ca).max(axis=2) > 8) & ((cb[..., 0] - cb[..., 2]) > 25)
        n8 = int((d > 8).sum())
        tag = ("FROZEN" if n8 < 8000 else
               "near-frozen" if n8 < 40000 else "moving")
        print(f"  f{i:04d}->f{i+1:04d} {(i-12)*33.333:>8.1f} | {d.mean():8.3f} {n8:8} "
              f"{int((d>25).sum()):8} {d.max():6.0f} | {int(warm.sum()):10} {tag}")


go("r2", 9, 22, "ROUND 2")
go("r1", 9, 20, "ROUND 1")

# what fraction of the screen is the only thing that changes during the hold?
D = os.path.join(AJ, "shots/r2/camera")
a = np.asarray(Image.open(f"{D}/f0012.jpg").convert("L"), dtype=np.float64)
b = np.asarray(Image.open(f"{D}/f0013.jpg").convert("L"), dtype=np.float64)
c = np.asarray(Image.open(f"{D}/f0014.jpg").convert("L"), dtype=np.float64)
for lab, x, y in (("f12->f13", a, b), ("f12->f14", a, c)):
    d = np.abs(y - x) > 8
    d = ndimage.binary_opening(d, np.ones((3, 3)))
    n = int(d.sum())
    if n:
        ys, xs = np.nonzero(d)
        print(f"\n{lab}: {n} px change >8 ({100*n/1024**2:.3f}% of frame), "
              f"bbox x[{xs.min()}..{xs.max()}] y[{ys.min()}..{ys.max()}], "
              f"extent {xs.max()-xs.min()}x{ys.max()-ys.min()} px "
              f"({(xs.max()-xs.min())/1024*100:.1f}% of frame width)")
    else:
        print(f"\n{lab}: nothing changed above threshold")
