"""Push aftermath HDR colours through the project's actual post chain.

GameDaylight Volume Profile: Bloom(threshold 1.8, intensity 0.5, clamp 8),
Tonemapping mode 1 = Neutral, ColorAdjustments(postExposure 0, contrast +14,
saturation +8). Reproduces URP LutBuilderHdr ordering closely enough to predict
final 8-bit values.
"""

import numpy as np

ACEScc_MIDGRAY = 0.4135884


def linear_to_logc(x):
    # ArriLogC V3, URP's default non-precise branch: unconditional log10.
    a, b, c, d = 5.555556, 0.047996, 0.244161, 0.386036
    return c * np.log10(np.maximum(a * x + b, 1e-10)) + d


def logc_to_linear(x):
    a, b, c, d = 5.555556, 0.047996, 0.244161, 0.386036
    return (np.power(10.0, (x - d) / c) - b) / a


def contrast(x, midpoint, amount):
    return (x - midpoint) * amount + midpoint


def saturation(x, amount):
    lum = x[..., 0] * 0.2126729 + x[..., 1] * 0.7151522 + x[..., 2] * 0.0721750
    return lum[..., None] + (x - lum[..., None]) * amount


def neutral_curve(x, a, b, c, d, e, f_):
    return ((x * (a * x + c * b) + d * e) / (x * (a * x + b) + d * f_)) - e / f_


def neutral_tonemap(x):
    a, b, c, d, e, f_ = 0.2, 0.29, 0.24, 0.272, 0.02, 0.3
    white_level, white_clip = 5.3, 1.0
    white_scale = 1.0 / neutral_curve(white_level, a, b, c, d, e, f_)
    x = neutral_curve(np.maximum(x, 0.0) * white_scale, a, b, c, d, e, f_)
    return (x * white_scale) / white_clip


def linear_to_srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def srgb_to_linear(x):
    x = np.asarray(x, dtype=float)
    return np.where(x <= 0.04045, x / 12.92, np.power((x + 0.055) / 1.055, 2.4))


def grade(linear_rgb):
    x = np.atleast_2d(np.asarray(linear_rgb, dtype=float))
    log = linear_to_logc(np.maximum(x, 1e-6))
    log = contrast(log, ACEScc_MIDGRAY, 1.0 + 14.0 / 100.0)
    x = np.maximum(logc_to_linear(log), 0.0)
    x = np.maximum(saturation(x, 1.0 + 8.0 / 100.0), 0.0)
    x = neutral_tonemap(x)
    return linear_to_srgb(x)


def to8(x):
    return np.round(np.clip(x, 0, 1) * 255).astype(int)


def luma8(rgb8):
    return 0.2126 * rgb8[..., 0] + 0.7152 * rgb8[..., 1] + 0.0722 * rgb8[..., 2]


# ---------------------------------------------------------------- floor solve
# The critic measured the clean floor at rgb (168,184,188), luminance 181.
# Invert the chain numerically to recover the scene-linear value it came from.
target = np.array([168, 184, 188], dtype=float) / 255.0


def solve_floor():
    lo, hi = np.zeros(3), np.full(3, 4.0)
    for _ in range(80):
        mid = 0.5 * (lo + hi)
        out = grade(mid)[0]
        hi = np.where(out > target, mid, hi)
        lo = np.where(out > target, lo, mid)
    return 0.5 * (lo + hi)


floor_linear = solve_floor()
floor_srgb = grade(floor_linear)[0]
floor_lum = luma8(to8(floor_srgb))
print(f"floor scene-linear {floor_linear.round(4)} -> {to8(floor_srgb)} lum {floor_lum:.1f}")

# ------------------------------------------------------------- board average
# Critic's "board luminance" reference is the whole plate, not the bare floor.
# Approximate it from the measured comparison: their round-1 peak was 1.22x
# board at a measured 8-bit peak; use the floor as an upper bound and report
# ratios against both so the target is unambiguous.
BOARD_MEAN_LUM = 120.0  # dark blocks + lit floor, from earlier plate stats

print()
print("=== aftermath emissives through the chain ===")

CASES = {
    # coals: EmberColor 3.4,0.34,0.02 and CoreColor 6.4,3.8,1.1, drawn additively
    # over the scorched floor. Intensity peaks at CoalIntensity 1.2 * ~1.0.
    "coal ember mid (hot=0.5, I=1.2)": (np.array([3.4, 0.34, 0.02]) * 0.5 * 1.2, 0.25),
    "coal ember hot (hot=0.85, I=1.2)": (np.array([3.4, 0.34, 0.02]) * 0.85 * 1.2, 0.25),
    "coal core (hot=1.0, I=1.2)": (np.array([6.4, 3.8, 1.1]) * 1.2, 0.25),
    # sparks: additive over floor or over smoke.
    "spark tail (I=1.0)": (np.array([2.6, 0.78, 0.09]) * 1.0, 1.0),
    "spark tail (I=1.35)": (np.array([2.6, 0.78, 0.09]) * 1.35, 1.0),
    "spark head (tail+core, I=1.35)": (
        (np.array([2.6, 0.78, 0.09]) + np.array([5.0, 4.3, 3.2])) * 1.35,
        1.0,
    ),
    "mote tail (I=0.7)": (np.array([2.6, 0.78, 0.09]) * 0.7, 1.0),
}

for name, (emissive, backdrop_scale) in CASES.items():
    # additive over whatever it sits on: scorched floor (dark) or clean floor
    backdrop = floor_linear * backdrop_scale
    scene = emissive + backdrop
    out8 = to8(grade(scene)[0])
    lum = luma8(out8)
    mx, mn = out8.max(), out8.min()
    sat = 0.0 if mx == 0 else (mx - mn) / mx
    blooms = "yes" if scene.max() > 1.8 else "NO"
    print(
        f"{name:34s} scene {np.round(scene,2)} -> rgb {out8} "
        f"lum {lum:5.1f}  {lum/BOARD_MEAN_LUM:.2f}x board  {lum/floor_lum:.2f}x floor  "
        f"sat {sat:.2f}  bloom {blooms}"
    )

# ------------------------------------------------------------------- scorch
print()
print("=== scorch mark depth ===")
MARK = np.array([0.115, 0.042, 0.018])
EDGE = np.array([0.22, 0.13, 0.08])
for label, col, alpha in [
    ("core  (a=0.82)", MARK, 0.82),
    ("mid   (a=0.60)", 0.5 * (MARK + EDGE), 0.60),
    ("rim   (a=0.28)", EDGE, 0.28),
]:
    # alpha blend happens in scene-linear before grading
    scene = col * alpha + floor_linear * (1 - alpha)
    out8 = to8(grade(scene)[0])
    lum = luma8(out8)
    print(f"scorch {label}: rgb {out8} lum {lum:5.1f}  delta {lum-floor_lum:+.1f} vs floor")

# -------------------------------------------------------------------- smoke
print()
print("=== smoke value range ===")
LIT = np.array([0.400, 0.346, 0.280])
SHADOW = np.array([0.005, 0.0041, 0.0033])
for tone in (0.86, 1.0, 1.08):
    vals = []
    for shade in (0.0, 0.25, 0.5, 0.75, 1.0):
        col = (SHADOW + (LIT - SHADOW) * shade) * tone
        out8 = to8(grade(col)[0])
        vals.append(luma8(out8))
    print(
        f"tone {tone:.2f}: shadow->lit lum {[round(v) for v in vals]} "
        f"spread {max(vals)-min(vals):.0f}  (floor {floor_lum:.0f})"
    )
