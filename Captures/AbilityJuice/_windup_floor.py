"""Reads the real floor under the wind-up's footprint, so the plate's multiply can be calibrated
against what is actually there rather than against a single averaged number."""

from pathlib import Path

import numpy as np
from PIL import Image

SHOT = Path(__file__).parent / "shots" / "r1" / "windup"
OUT = Path(__file__).parent / "_r2_floor_patches.png"


def luma(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx = a.max(axis=-1)
    mn = a.min(axis=-1)
    return np.where(mx > 1e-3, (mx - mn) / np.maximum(mx, 1e-3), 0.0)


clean = np.asarray(Image.open(SHOT / "f0000.jpg").convert("RGB")).astype(np.float32)

# The 3x3 plate covers roughly 513 px centred on the frame. Sample eight patches around the unit
# that stands in the middle, then keep only the ones that are actually floor.
patches = {}
for name, (y, x) in {
    "N": (300, 500),
    "NE": (330, 640),
    "E": (520, 660),
    "SE": (660, 640),
    "S": (680, 500),
    "SW": (660, 360),
    "W": (520, 340),
    "NW": (330, 360),
}.items():
    patches[name] = clean[y : y + 46, x : x + 46]

print("patch    luma    sat    rgb")
keep = []
for name, patch in patches.items():
    l = luma(patch).mean()
    print(
        f"  {name:3s}  {l:7.1f}  {sat(patch).mean():.3f}  "
        f"{patch.reshape(-1, 3).mean(axis=0).round(0)}   std {luma(patch).std():5.1f}"
    )
    if l > 120:
        keep.append(patch)

floor = np.concatenate([p.reshape(-1, 3) for p in keep])
print(
    f"\nfloor pool ({len(keep)} patches)  luma {luma(floor).mean():6.1f}  "
    f"sat {sat(floor).mean():.3f}  range {np.percentile(luma(floor), 5):.0f}"
    f"..{np.percentile(luma(floor), 95):.0f}"
)
np.save(Path(__file__).parent / "_r2_floor.npy", floor)

grid = np.concatenate([p.astype(np.uint8) for p in patches.values()], axis=1)
Image.fromarray(grid).save(OUT)
print(f"wrote {OUT}")
