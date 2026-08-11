"""Predicts what the rebuilt wind-up should measure, so round 2 has stated targets to be judged on.

Takes a clean pre-fire frame from the r1 capture as the real floor, applies the new plate's linear
multiply to it, and reports the luminance and saturation the critic will read back. Also prints the
countdown ring's radius schedule in cells and pixels.
"""

import math
from pathlib import Path

import numpy as np
from PIL import Image

SHOT = Path(__file__).parent / "shots" / "r1" / "windup"

CELL = 2.7
DURATION = 1.1
RADIUS = 4.3
RING_START_CELLS = 2.2
RING_EASE = 0.6
RING_FILL_RADIUS = 1.35

OPENING_TINT = np.array([0.66, 0.26, 0.075])
LOADED_TINT = np.array([0.58, 0.22, 0.055])
BULLSEYE_MUL = 0.74


def to_linear(a):
    a = a / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def to_srgb(a):
    out = np.where(a <= 0.0031308, a * 12.92, 1.055 * np.power(np.clip(a, 0, None), 1 / 2.4) - 0.055)
    return np.clip(out, 0, 1) * 255.0


def luma(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx = a.max(axis=-1)
    mn = a.min(axis=-1)
    return np.where(mx > 1e-3, (mx - mn) / np.maximum(mx, 1e-3), 0.0)


def lerp(a, b, t):
    return a + (b - a) * t


floor = np.load(Path(__file__).parent / "_r2_floor.npy")
print(
    f"floor under the mark   luma {luma(floor).mean():6.1f}  sat {sat(floor).mean():.3f}  "
    f"tile contrast (p95-p5) {np.percentile(luma(floor), 95) - np.percentile(luma(floor), 5):5.1f}"
)

print("\n  t     tintStrength   plate luma   plate sat   seam contrast   bullseye luma")
for t in (0.0, 0.15, 0.35, 0.55, 0.75, 0.95, 1.10):
    p = min(t / DURATION, 1.0)
    pressure = p**1.7
    strength = lerp(0.94, 1.0, min(p * 3.0, 1.0))
    tint = lerp(OPENING_TINT, LOADED_TINT, pressure)
    effective = lerp(np.ones(3), tint, strength)

    lit = to_srgb(to_linear(floor) * effective)
    bull = to_srgb(to_linear(floor) * effective * BULLSEYE_MUL)
    seam = np.percentile(luma(lit), 95) - np.percentile(luma(lit), 5)
    print(
        f"{t:5.2f}      {strength:.3f}       {luma(lit).mean():7.1f}      {sat(lit).mean():.3f}"
        f"        {seam:6.1f}          {luma(bull).mean():6.1f}"
    )

# Pixels per world unit in the 1024 capture: 60 degree FOV at 14 units, 73 degree pitch.
px = 1024.0 / (2.0 * 14.0 * math.tan(math.radians(30.0)))
print(f"\nscale: {px:.1f} px per world unit, {px * CELL:.0f} px per cell")

start = RING_START_CELLS * CELL
print("\n  t      ring radius (units / cells / px)   thickness   outer px   bright bbox px")
for t in (0.0, 0.2, 0.4, 0.6, 0.8, 0.93, 1.0, 1.05, 1.10):
    p = min(t / DURATION, 1.0)
    r = start * (max(0.0, 1.0 - p) ** RING_EASE)
    thick = max(
        lerp(0.22, 0.46, p**1.5),
        lerp(0.0, 0.98, min(max((RING_FILL_RADIUS - r) / RING_FILL_RADIUS, 0.0), 1.0)),
    )
    outer = r + thick * 0.5
    print(
        f"{t:5.2f}       {r:5.2f} / {r / CELL:4.2f} / {r * px:5.0f}          {thick:.2f}"
        f"       {outer * px:5.0f}       {2 * outer * px:6.0f}"
    )

print("\ncells inside the danger area (radius 4.3, cell 2.7):")
live = []
for iz in range(-3, 4):
    row = ""
    for ix in range(-3, 4):
        d = math.hypot(ix * CELL, iz * CELL)
        inside = d <= RADIUS
        row += "#" if inside else "."
        if inside:
            live.append((ix, iz))
    print("   " + row)
xs = [c[0] for c in live]
print(
    f"   {len(live)} cells, {max(xs) - min(xs) + 1} x {max(xs) - min(xs) + 1} block, "
    f"{(max(xs) - min(xs) + 1) * CELL:.1f} units across = {(max(xs) - min(xs) + 1) * CELL * px:.0f} px"
)
