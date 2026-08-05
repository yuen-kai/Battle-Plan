"""Round-2 shockwave: full re-measurement from raw frames (float target, bloom now live)."""
import numpy as np
from PIL import Image

ROOT = "Captures/AbilityJuice/shots/r2/shockwave"
N = 55
CELL_PX = None  # solved below
PICKS = {1: 13, 2: 17, 3: 30}


def load(i):
    return np.asarray(Image.open(f"{ROOT}/f{i:04d}.jpg")).astype(np.float64)


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx = a.max(axis=-1)
    mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


F = [load(i) for i in range(N)]
L = [lum(f) for f in F]
plate = np.median(np.stack(F[0:11]), axis=0)
Lp = lum(plate)
h, w = Lp.shape
Y, X = np.mgrid[0:h, 0:w]

print("=" * 78)
print("A. BOARD BASELINE  (the yardstick everything is judged against)")
print("=" * 78)
# Floor-only mask near the effect: exclude the dark crates. Use plate luminance window.
near = (np.abs(X - 512) < 340) & (np.abs(Y - 540) < 330)
floor = near & (Lp > 150)
print(f"floor pixels sampled: {floor.sum()}")
print(f"floor luminance: mean {Lp[floor].mean():.1f}  median {np.median(Lp[floor]):.1f} "
      f" p5 {np.percentile(Lp[floor],5):.1f}  p95 {np.percentile(Lp[floor],95):.1f}")
print(f"floor saturation: mean {sat(plate)[floor].mean():.3f}")

# tile-to-tile variation: mean luminance of each tile interior, spread across tiles
tile_means = []
for ty in range(3):
    for tx in range(3):
        x0 = 240 + tx * 190
        y0 = 270 + ty * 190
        blk = Lp[y0 + 40:y0 + 150, x0 + 40:x0 + 150]
        if blk.size and blk.min() > 150:
            tile_means.append(blk.mean())
tile_means = np.array(tile_means)
print(f"tile-to-tile variation: {len(tile_means)} tiles, "
      f"range {tile_means.min():.1f}-{tile_means.max():.1f}, spread {tile_means.max()-tile_means.min():.1f}")

print()
print("=" * 78)
print("B. GEOMETRY / SCALE")
print("=" * 78)
# max change mask over the run -> outer extent
dmax = np.zeros_like(Lp)
for i in range(12, N):
    dmax = np.maximum(dmax, np.abs(L[i] - Lp))
m = dmax > 25
ys, xs = np.where(m)
cx = (xs.min() + xs.max()) / 2
cy = (ys.min() + ys.max()) / 2
ax = (xs.max() - xs.min()) / 2
ay = (ys.max() - ys.min()) / 2
print(f"effect centre ({cx:.1f},{cy:.1f})  semi-axes x {ax:.1f}px  y {ay:.1f}px")
WORLD_R = 4.3  # ImpactShockwave.Spawn(..., maxRadius: 4.3f) from AbilityFilmStudio
CELL_WORLD = 2.7
pxpu = ax / WORLD_R
CELL_PX = pxpu * CELL_WORLD
print(f"maxRadius 4.3 world units -> {pxpu:.2f} px/unit -> cell = {CELL_PX:.1f} px")
print(f"effect footprint = {2*ax/CELL_PX:.2f} cells wide, radius {ax/CELL_PX:.2f} cells")

# elliptical normalized radius, in world units
R = np.sqrt(((X - cx) / ax) ** 2 + ((Y - cy) / ay) ** 2) * WORLD_R
np.save("Captures/AbilityJuice/_r2_shock_R.npy", R)
np.save("Captures/AbilityJuice/_r2_shock_meta.npy", np.array([cx, cy, ax, ay, pxpu, CELL_PX]))
