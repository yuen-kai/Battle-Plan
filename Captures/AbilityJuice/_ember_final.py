"""Verify the final chosen aftermath colours through the project's post chain."""

import numpy as np

from _ember_tonemap import BOARD_MEAN_LUM, floor_linear, grade, luma8, to8

FLOOR_LUM = luma8(to8(grade(floor_linear)[0]))


def show(label, scene):
    out8 = to8(grade(np.atleast_2d(scene))[0])
    lum = luma8(out8)
    sat = 0.0 if out8.max() == 0 else (out8.max() - out8.min()) / out8.max()
    print(
        f"{label:32s} rgb {out8} lum {lum:5.1f} "
        f"{lum / BOARD_MEAN_LUM:.2f}x board  sat {sat:.2f}"
    )
    return lum, sat


SPARK_TAIL = np.array([2.80, 0.65, 0.14])
SPARK_CORE = np.array([2.60, 1.95, 1.15])
COAL_EMBER = np.array([3.60, 0.50, 0.055])
COAL_CORE = np.array([6.40, 3.80, 1.10])
COAL_CORE_CUT = 0.86
SMOKE_LIT = np.array([0.400, 0.346, 0.280])
SMOKE_SHADOW = np.array([0.020, 0.0164, 0.0132])

print("=== spark, premultiplied over floor (coverage 1) ===")
peak_sat = []
for inten in (0.45, 0.7, 1.0, 1.35):
    for heat in (0.4, 0.75, 1.0):
        lum, sat = show(f"tail I={inten:.2f} heat={heat:.2f}", SPARK_TAIL * heat * inten)
        if inten >= 0.7 and heat >= 0.75:
            peak_sat.append((lum, sat))
for core in (0.3, 0.6, 1.0):
    show(f"head core={core:.2f}", (SPARK_TAIL + SPARK_CORE * core * core) * 1.2)

print()
print("=== spark, premultiplied over smoke (coverage 0.55, smoke behind) ===")
smoke_mid = SMOKE_SHADOW + (SMOKE_LIT - SMOKE_SHADOW) * 0.45
for heat in (0.6, 1.0):
    cov = 0.55
    show(f"tail heat={heat:.2f} cov=0.55", SPARK_TAIL * heat * cov + smoke_mid * (1 - cov))

print()
print("=== coal bed, additive over scorch core ===")
MARK = np.array([0.115, 0.042, 0.018])
scorched = MARK * 0.82 + floor_linear * 0.18
show("scorch core (no fire)", scorched)
best_orange = (0, 0)
for hot in (0.25, 0.45, 0.65, 0.8, 0.86, 0.92, 1.0):
    mix = min(1.0, max(0.0, (hot - COAL_CORE_CUT) / (1 - COAL_CORE_CUT)))
    col = COAL_EMBER * (1 - mix) + COAL_CORE * mix
    lum, sat = show(f"coal hot={hot:.2f}", col * hot * 1.2 + scorched)
    if sat >= 0.6 and lum > best_orange[0]:
        best_orange = (lum, sat)
print(f"  brightest genuinely-orange coal: lum {best_orange[0]:.1f} "
      f"= {best_orange[0] / BOARD_MEAN_LUM:.2f}x board, sat {best_orange[1]:.2f}")

print()
print("=== smoke value ladder (tone 1.0) ===")
lums = []
for shade in np.linspace(0, 1, 11):
    col = SMOKE_SHADOW + (SMOKE_LIT - SMOKE_SHADOW) * shade
    lums.append(luma8(to8(grade(col)[0])))
print("  lum ladder:", [round(v) for v in lums])
print(f"  min {min(lums):.0f}  max {max(lums):.0f}  floor {FLOOR_LUM:.0f}")
print(f"  uniform-shade std {np.std(lums):.1f} (critic wants 30-45)")

print()
print("=== scorch depth vs floor (critic wants -50 to -70) ===")
EDGE = np.array([0.22, 0.13, 0.08])
for label, col, alpha in [
    ("core a=0.82", MARK, 0.82),
    ("core a=0.72", MARK, 0.72),
    ("mid  a=0.55", 0.5 * (MARK + EDGE), 0.55),
    ("rim  a=0.28", EDGE, 0.28),
]:
    scene = col * alpha + floor_linear * (1 - alpha)
    lum = luma8(to8(grade(scene)[0]))
    print(f"  scorch {label}: lum {lum:5.1f}  delta {lum - FLOOR_LUM:+.1f}")
