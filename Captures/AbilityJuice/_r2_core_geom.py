"""Round-2 impact core: camera geometry + empirical cell calibration."""

import math
import os

import numpy as np
from PIL import Image

RAW = "Captures/AbilityJuice/shots/r2/impactcore"
CELL = 2.7
FOV = 60.0
PITCH = 73.0
DIST = 11.0
PX = 1024

# --- analytic ---------------------------------------------------------------
half_h = DIST * math.tan(math.radians(FOV / 2))
px_per_unit = PX / (2 * half_h)
print(f"frustum half-height at look-at depth: {half_h:.4f} u  -> {px_per_unit:.2f} px/unit")
print(f"world-X (screen horizontal) cell width : {CELL * px_per_unit:7.1f} px @1024")
fore = math.sin(math.radians(PITCH))
print(f"ground tilt foreshortening cos(17deg)  : {fore:.4f}")
print(f"world-Z (screen vertical) cell height  : {CELL * px_per_unit * fore:7.1f} px @1024")
print(f"same, in a 512px strip panel           : {CELL * px_per_unit / 2:7.1f} px wide, "
      f"{CELL * px_per_unit * fore / 2:7.1f} px tall")

# --- empirical: seam spacing on a clean frame -------------------------------
clean = np.asarray(Image.open(os.path.join(RAW, "f0000.jpg")).convert("RGB")).astype(np.float32)
lum = 0.2126 * clean[..., 0] + 0.7152 * clean[..., 1] + 0.0722 * clean[..., 2]


def troughs(profile, min_gap=60):
    """Local minima of a 1-D profile, i.e. seam centres."""
    sm = np.convolve(profile, np.ones(5) / 5, mode="same")
    out = []
    for i in range(3, len(sm) - 3):
        w = sm[i - 3 : i + 4]
        if sm[i] == w.min() and sm[i] < sm.mean() - 3:
            if not out or i - out[-1] >= min_gap:
                out.append(i)
            elif sm[i] < sm[out[-1]]:
                out[-1] = i
    return out


# horizontal scan across an unobstructed band of floor
band = lum[600:700, :].mean(axis=0)
hs = troughs(band)
print("\nhorizontal seam x-positions (row band 600-700):", hs)
if len(hs) > 1:
    d = np.diff(hs)
    print("  spacings:", d, " mean %.1f px" % d.mean())

col = lum[:, 120:220].mean(axis=1)
vs = troughs(col)
print("vertical seam y-positions (col band 120-220):", vs)
if len(vs) > 1:
    d = np.diff(vs)
    print("  spacings:", d, " mean %.1f px" % d.mean())

print("\nfloor luminance stats: mean %.1f  p05 %.1f  p95 %.1f"
      % (lum.mean(), np.percentile(lum, 5), np.percentile(lum, 95)))
