"""Find spark/coal HDR values that land as genuine orange above 1.6x board.

Additive blending over the board floor (scene-linear 0.428,0.545,0.580) floors
the green and blue channels, so the most saturated an additive spark can ever
be over clean floor is (255,184,188) -> saturation 0.28. That is round one's
cream sliver and it is a property of the blend mode, not of the colour. A
premultiplied-alpha spark occludes the floor first and has no such ceiling.
"""

import numpy as np

from _ember_tonemap import BOARD_MEAN_LUM, floor_linear, grade, luma8, to8


def report(label, scene):
    out8 = to8(grade(scene)[0])
    lum = luma8(out8)
    sat = 0.0 if out8.max() == 0 else (out8.max() - out8.min()) / out8.max()
    print(
        f"{label:38s} scene {np.round(scene, 2)} -> rgb {out8} "
        f"lum {lum:5.1f}  {lum / BOARD_MEAN_LUM:.2f}x board  sat {sat:.2f}"
    )
    return lum, sat


print("=== ceiling of ADDITIVE over clean floor ===")
for boost in (2, 4, 8, 16):
    report(f"additive pure red x{boost}", np.array([boost, 0.0, 0.0]) + floor_linear)

print()
print("=== PREMULTIPLIED (spark occludes floor, coverage = 1) ===")
grid = []
for r in np.arange(1.0, 6.01, 0.1):
    for g in np.arange(0.1, 2.51, 0.05):
        for b in np.arange(0.0, 0.61, 0.05):
            grid.append((r, g, b))
grid = np.array(grid)
out = grade(grid)
out8 = np.round(np.clip(out, 0, 1) * 255)
lum = luma8(out8)
sat = np.where(out8.max(1) == 0, 0, (out8.max(1) - out8.min(1)) / np.maximum(out8.max(1), 1))
# genuine orange: hue between red and yellow, strongly saturated, above 1.6x board
orange = (out8[:, 0] >= out8[:, 1]) & (out8[:, 1] >= out8[:, 2])
ok = orange & (sat >= 0.62) & (lum >= 1.62 * BOARD_MEAN_LUM)
print(f"{ok.sum()} of {len(grid)} candidates satisfy sat>=0.62 and lum>=1.62x board")
if ok.any():
    idx = np.argsort(-sat[ok])[:6]
    chosen = grid[ok][idx]
    for c in chosen:
        report("candidate", c)

print()
print("=== chosen spark tail ramp (premultiplied) ===")
TAIL = np.array([3.05, 0.92, 0.10])
CORE = np.array([5.4, 4.1, 2.4])
for heat in (0.35, 0.6, 0.85, 1.0):
    report(f"tail heat={heat:.2f}", TAIL * heat)
for core in (0.4, 0.7, 1.0):
    report(f"head core={core:.2f}", TAIL + CORE * core * core)

print()
print("=== coal ramp over scorched floor ===")
MARK = np.array([0.115, 0.042, 0.018])
scorched = MARK * 0.82 + floor_linear * 0.18
print(f"scorched backdrop scene-linear {scorched.round(3)} -> rgb {to8(grade(scorched)[0])}")
EMBER = np.array([3.4, 0.34, 0.02])
COAL_CORE = np.array([6.4, 3.8, 1.1])
for hot in (0.3, 0.5, 0.7, 0.85, 0.95, 1.0):
    mix = min(1.0, max(0.0, (hot - 0.72) / 0.28))
    col = EMBER * (1 - mix) + COAL_CORE * mix
    report(f"coal hot={hot:.2f}", col * hot * 1.2 + scorched)

print()
print("=== smoke shadow floor ===")
LIT = np.array([0.400, 0.346, 0.280])
for shadow_scale in (0.005, 0.012, 0.020, 0.030, 0.045):
    sh = np.array([shadow_scale, shadow_scale * 0.82, shadow_scale * 0.66])
    lo = luma8(to8(grade(sh)[0]))
    hi = luma8(to8(grade(LIT)[0]))
    mid = luma8(to8(grade(sh + (LIT - sh) * 0.5)[0]))
    print(
        f"shadow {shadow_scale:.3f} -> lum {lo:5.1f} | mid {mid:5.1f} | lit {hi:5.1f} "
        f"| span {hi - lo:.0f}"
    )
