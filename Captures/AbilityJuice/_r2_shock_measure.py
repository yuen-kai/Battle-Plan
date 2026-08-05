"""Round-2 shockwave measurement. Re-measured from scratch off raw frames."""
import numpy as np
from PIL import Image

ROOT = "Captures/AbilityJuice/shots/r2/shockwave"
N = 55
T0 = -0.4000
DT = 1.0 / 30.0


def load(i):
    return np.asarray(Image.open(f"{ROOT}/f{i:04d}.jpg")).astype(np.float64)


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx = a.max(axis=-1)
    mn = a.min(axis=-1)
    return np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


frames = [load(i) for i in range(N)]
L = [lum(f) for f in frames]

# --- clean plate: median of preroll frames 0..10 (event fires at frame 12)
plate = np.median(np.stack(frames[0:11]), axis=0)
Lp = lum(plate)

print("=== CLEAN PLATE ===")
print(f"plate lum mean {Lp.mean():.2f}  median {np.median(Lp):.2f}")

# --- locate the effect: biggest absolute change vs plate over the run
dmax = np.zeros_like(Lp)
for i in range(12, N):
    dmax = np.maximum(dmax, np.abs(L[i] - Lp))
ys, xs = np.where(dmax > 25)
cx, cy = xs.mean(), ys.mean()
print(f"effect centroid approx ({cx:.0f},{cy:.0f})  bbox x[{xs.min()},{xs.max()}] y[{ys.min()},{ys.max()}]")

np.save("Captures/AbilityJuice/_r2_shock_plate.npy", plate)
np.save("Captures/AbilityJuice/_r2_shock_dmax.npy", dmax)
