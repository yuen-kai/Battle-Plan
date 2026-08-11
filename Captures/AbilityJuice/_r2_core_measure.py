"""Round-2 impact core: locate the strip's impact frame, then measure it hard."""

import os

import numpy as np
from PIL import Image

RAW = "Captures/AbilityJuice/shots/r2/impactcore"
STRIP = "Captures/AbilityJuice/strips/r2/impactcore.jpg"
PXPERCELL = 217.7  # world-X, 1024px frame, frame centre (verified against seams)


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def load(i):
    return np.asarray(Image.open(os.path.join(RAW, f"f{i:04d}.jpg")).convert("RGB")).astype(np.float32)


files = sorted(f for f in os.listdir(RAW) if f.endswith(".jpg"))
n = len(files)

# --- which raw frame is the strip's middle panel? ---------------------------
strip = np.asarray(Image.open(STRIP).convert("RGB")).astype(np.float32)
mid = strip[:, 520:1032]  # 512-wide centre panel
best, bestd = None, 1e18
for i in range(n):
    small = np.asarray(Image.fromarray(load(i).astype(np.uint8)).resize((512, 512), Image.LANCZOS)).astype(np.float32)
    d = np.abs(small - mid).mean()
    if d < bestd:
        best, bestd = i, d
print(f"strip impact panel == raw frame f{best:04d} (mad {bestd:.2f})")

# --- per-frame energy curve -------------------------------------------------
clean = load(0)
cl = lum(clean)
print("\nframe  peak  n>=254  n==255  meanLum  delta-vs-clean")
for i in range(n):
    a = load(i)
    L = lum(a)
    hard = (a >= 254).all(axis=-1).sum()
    full = (a >= 255).all(axis=-1).sum()
    print(f"f{i:04d}  {L.max():5.0f}  {hard:6d}  {full:6d}  {L.mean():7.1f}  {L.mean()-cl.mean():+7.2f}")
