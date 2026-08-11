"""Emulates BP_AftermathScorch and BP_AftermathCoals so the burn's depth against the floor and the
fire's coverage can be measured before either shader is compiled."""

import numpy as np
from PIL import Image

N = 512
FLOOR_LINEAR = 0.462
FLOOR_RGB = np.array([168.0, 184.0, 188.0])
FLOOR_LUM = 181.0


def frac(x):
    return x - np.floor(x)


def hash1(n):
    return frac(np.sin(n * 12.9898) * 43758.5453123)


def hash3(n):
    s = np.array([n * 12.9898, n * 78.233 + 2.7, n * 39.425 + 6.1])
    return frac(np.sin(s) * np.array([43758.5453, 22578.1459, 19642.3491]))


def value_noise(px, py):
    cx, cy = np.floor(px), np.floor(py)
    fx, fy = px - cx, py - cy
    fx = fx * fx * (3 - 2 * fx)
    fy = fy * fy * (3 - 2 * fy)
    a = hash1(cx + cy * 57.0)
    b = hash1(cx + 1 + cy * 57.0)
    c = hash1(cx + (cy + 1) * 57.0)
    d = hash1(cx + 1 + (cy + 1) * 57.0)
    return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy


def fbm(px, py, lac):
    total, amp = 0.0, 0.5
    for _ in range(3):
        total = total + value_noise(px, py) * amp
        px, py = px * lac, py * lac
        amp *= 0.5
    return total / 0.875


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def smooth_max(a, b, k):
    h = np.clip(0.5 + 0.5 * (a - b) / max(k, 1e-4), 0, 1)
    return b + (a - b) * h + k * h * (1 - h)


u = np.linspace(0, 1, N)
UU, VV = np.meshgrid(u, u)


def scorch(seed, opacity=0.72, rim_alpha=0.34, softness=0.13, core_depth=0.42,
           lobes=4, spread=0.19, min_lobe=0.17, max_lobe=0.32, knit=0.20,
           warp=0.09, warp_scale=3.1, noise_scale=7.5, mottle=0.30,
           spatter=0.55, ray_freq=3.4, fringe_cut=0.56,
           mark=(0.030, 0.023, 0.020), edge=(0.090, 0.074, 0.064)):
    rx, ry = UU - 0.5, VV - 0.5
    wx = fbm(rx * warp_scale + seed * 0.61, ry * warp_scale + seed * 0.61, 2.09) - 0.5
    wy = fbm(rx * warp_scale + seed * 0.61 + 7.9, ry * warp_scale + seed * 0.61 + 7.9, 2.09) - 0.5
    px, py = rx + wx * warp, ry + wy * warp

    unioned = np.full((N, N), -8.0)
    for i in range(lobes):
        h = hash3(seed * 2.7 + i * 5.13 + 0.9)
        s = spread * 0.3 if i == 0 else spread
        cx, cy = (h[0] - 0.5) * s, (h[1] - 0.5) * s
        rad = max_lobe if i == 0 else min_lobe + (max_lobe * 0.82 - min_lobe) * h[2]
        unioned = smooth_max(unioned, 1 - np.hypot(px - cx, py - cy) / max(rad, 1e-4), knit)

    mot = fbm(px * noise_scale + seed * 4.3, py * noise_scale + seed * 4.3, 2.09)
    boundary = smoothstep(0.0, softness, unioned)
    core = np.clip(unioned / core_depth, 0, 1)
    alpha = opacity * boundary * (rim_alpha + (1 - rim_alpha) * core) * ((1 - mottle) + mottle * mot)

    angle = np.arctan2(ry, rx)
    radial = np.hypot(rx, ry)
    rays = fbm(angle * ray_freq + seed * 1.7, radial * 11.0 + seed * 1.7, 2.09)
    band = smoothstep(-0.30, -0.16, unioned) * (1 - smoothstep(-0.06, 0.05, unioned))
    fringe = smoothstep(fringe_cut, fringe_cut + 0.09, rays) * band * (1 - smoothstep(0.36, 0.50, radial))
    alpha = np.maximum(alpha, fringe * opacity * spatter)

    colour = np.stack([np.array(edge)[c] + (np.array(mark)[c] - np.array(edge)[c]) * core
                       for c in range(3)], axis=-1)
    return colour, np.clip(alpha, 0, 1)


def coals(seed, intensity=1.2, crack_scale=7.5, threshold=0.58, slope=0.34,
          feather=0.15, falloff=1.6, core_cut=0.58, warp=0.32,
          ember=(2.4, 0.42, 0.05), core=(5.6, 3.4, 1.15)):
    px, py = UU - 0.5, VV - 0.5
    radial = np.hypot(px, py) * 2
    wx = fbm(px * 4 + seed * 1.3, py * 4 + seed * 1.3, 2.13) - 0.5
    wy = fbm(px * 4 + seed * 1.3 + 5.5, py * 4 + seed * 1.3 + 5.5, 2.13) - 0.5
    heat = fbm((px + wx * warp) * crack_scale + seed * 2.9,
               (py + wy * warp) * crack_scale + seed * 2.9, 2.13)
    thr = threshold + radial * radial * slope
    hot = np.clip((heat - thr) / feather, 0, 1)
    hot = np.where(radial < 1, hot, 0) ** falloff
    mix = np.clip((hot - core_cut) / (1 - core_cut), 0, 1)
    colour = np.stack([np.array(ember)[c] + (np.array(core)[c] - np.array(ember)[c]) * mix
                       for c in range(3)], axis=-1)
    return colour * hot[..., None] * intensity, hot


def encode(linear):
    return 255 * np.clip(linear, 0, 1) ** (1 / 2.2)


def lum(rgb):
    return 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]


floor_linear_rgb = (FLOOR_RGB / 255.0) ** 2.2

print("SCORCH")
print(f"{'seed':>5} {'span':>5} {'coreL':>6} {'dCore':>6} {'meanL':>6} {'dMean':>6} {'p5':>5} "
      f"{'edgepx':>6}")
panels = []
for seed in (3.1, 9.7, 17.4, 26.2):
    colour, alpha = scorch(seed)
    out = encode(colour * alpha[..., None] + floor_linear_rgb * (1 - alpha[..., None]))
    L = lum(out)
    drawn = alpha > 0.02
    ys, xs = np.where(drawn)
    span = max(xs.max() - xs.min(), ys.max() - ys.min()) / N
    body = alpha > 0.35
    edgepx = drawn[0].sum() + drawn[-1].sum() + drawn[:, 0].sum() + drawn[:, -1].sum()
    core_mask = alpha > np.percentile(alpha[drawn], 88)
    print(f"{seed:5.1f} {span:5.2f} {L[core_mask].mean():6.1f} "
          f"{L[core_mask].mean() - FLOOR_LUM:6.1f} {L[body].mean():6.1f} "
          f"{L[body].mean() - FLOOR_LUM:6.1f} {np.percentile(L[drawn], 5):5.0f} {edgepx:6d}")
    panels.append(Image.fromarray(np.uint8(out)).resize((300, 300)))

print()
print("COALS over the burn (the burn is what makes additive fire read as saturated)")
# The coal quad is smaller than the mark quad, so the burn under it is at its deepest.
burnt = np.array([0.030, 0.023, 0.020]) * 0.62 + floor_linear_rgb * 0.38
for label, seed, kw in [
    ("as authored", 3.1, {}),
    ("as authored", 9.7, {}),
    ("thr .52", 3.1, dict(threshold=0.52, slope=0.28)),
    ("thr .52", 9.7, dict(threshold=0.52, slope=0.28)),
    ("thr .48", 3.1, dict(threshold=0.48, slope=0.26)),
]:
    colour, hot = coals(seed, **kw)
    out = encode(colour + burnt)
    L = lum(out)
    lit = hot > 0.02
    clipped = (out >= 254).all(axis=-1)
    warm = (out[..., 0] - out[..., 2]) > 30
    sat = (out.max(axis=-1) - out.min(axis=-1)) / np.maximum(out.max(axis=-1), 1)
    print(f"{label:>12} seed {seed:4.1f}: lit {lit.mean() * 100:5.1f}% of quad, "
          f"warm {warm.mean() * 100:5.1f}%, clipped-white {clipped.mean() * 100:4.2f}%, "
          f"sat(lit) {sat[lit].mean():.2f}, peak {L.max():.0f} ({L.max() / FLOOR_LUM:.2f}x)")
    panels.append(Image.fromarray(np.uint8(out)).resize((300, 300)))

sheet = Image.new("RGB", (300 * len(panels), 300), tuple(FLOOR_RGB.astype(int)))
for i, p in enumerate(panels):
    sheet.paste(p, (i * 300, 0))
sheet.save("_r2_ground.png")
print("saved _r2_ground.png")
