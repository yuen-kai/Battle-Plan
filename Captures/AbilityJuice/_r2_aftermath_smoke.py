"""Emulates BP_AftermathSmoke.frag on the CPU so the silhouette and the internal luminance spread
can be measured before the shader is ever compiled. The critic measured 5.1 inside a round-1 lobe
against Clash Mini's 30-45; this reports the same statistic."""

import numpy as np
from PIL import Image

N = 512
FLOOR_LUM = 181.0

LIGHT = np.array([-0.51, 0.66, 0.55])
LIGHT /= np.linalg.norm(LIGHT)


def frac(x):
    """HLSL frac: always in [0, 1), unlike numpy's signed modf."""
    return x - np.floor(x)


def hash1(n):
    return frac(np.sin(n * 12.9898) * 43758.5453123)


def hash3(n):
    s = np.array([n * 12.9898, n * 78.233 + 4.1, n * 39.425 + 9.7])
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


def fbm(px, py):
    total, amp = 0.0, 0.5
    for _ in range(3):
        total = total + value_noise(px, py) * amp
        px, py = px * 2.17, py * 2.17
        amp *= 0.5
    return total / 0.875


def smooth_max(a, b, k):
    h = np.clip(0.5 + 0.5 * (a - b) / max(k, 1e-4), 0, 1)
    return b + (a - b) * h + k * h * (1 - h)


def render(seed, lobes, erode, tear, opacity, lit, shadow, tone,
           spread=0.34, min_lobe=0.115, max_lobe=0.27, knit=0.16,
           warp_amt=0.065, warp_scale=2.4, noise_scale=5.5, mottle=0.30,
           shade_low=0.18, shade_span=0.44, rim=0.74, crease=0.52,
           sweep=2.4, lobe_weight=0.55):
    u = np.linspace(0, 1, N)
    uu, vv = np.meshgrid(u, u)
    px, py = uu - 0.5, vv - 0.5
    wx = fbm(px * warp_scale + seed * 0.83, py * warp_scale + seed * 0.83) - 0.5
    wy = fbm(px * warp_scale + seed * 0.83 + 13.7, py * warp_scale + seed * 0.83 + 13.7) - 0.5
    px, py = px + wx * warp_amt, py + wy * warp_amt

    unioned = np.full((N, N), -8.0)
    best = np.full((N, N), -8.0)
    bcx = np.zeros((N, N))
    bcy = np.zeros((N, N))
    brad = np.full((N, N), max_lobe)
    btone = np.ones((N, N))

    for i in range(lobes):
        h = hash3(seed * 3.11 + i * 7.77 + 1.3)
        cx = (h[0] - 0.5) * (spread * 0.22 if i == 0 else spread)
        cy = (h[1] - 0.5) * (spread * 0.22 if i == 0 else spread)
        rad = max_lobe if i == 0 else min_lobe + (max_lobe * 0.74 - min_lobe) * h[2]
        lobe = 1.0 - np.hypot(px - cx, py - cy) / max(rad, 1e-4)
        unioned = smooth_max(unioned, lobe, knit)
        take = lobe > best
        best = np.where(take, lobe, best)
        bcx = np.where(take, cx, bcx)
        bcy = np.where(take, cy, bcy)
        brad = np.where(take, rad, brad)
        btone = np.where(take, 0.80 + 0.42 * hash1(seed * 5.7 + i * 3.31), btone)

    detail = fbm(px * noise_scale + seed * 2.3, py * noise_scale + seed * 2.3)
    field = unioned - erode - tear * (detail - 0.34)
    edge_width = 0.024
    alpha = np.clip(field / edge_width, 0, 1) * opacity

    lx, ly = (px - bcx) / brad, (py - bcy) / brad
    lz = np.sqrt(np.clip(1 - (lx * lx + ly * ly), 0, 1))
    nx, ny, nz = lx, ly, lz + 0.10
    norm = np.sqrt(nx * nx + ny * ny + nz * nz)
    lam = np.clip((nx * LIGHT[0] + ny * LIGHT[1] + nz * LIGHT[2]) / norm, 0, 1)
    t = np.clip((lam - shade_low) / max(shade_span, 1e-3), 0, 1)
    lobe_term = t * t * (3 - 2 * t)
    flat = np.hypot(LIGHT[0], LIGHT[1])
    mass_term = np.clip(0.5 + (px * LIGHT[0] + py * LIGHT[1]) / flat * sweep, 0, 1)
    # Density pockets ride on the shade term, not on the colour, so a thinning remnant keeps a lit
    # side and a dark side instead of flattening into one value.
    shade = np.clip(
        lobe_term * lobe_weight + mass_term * (1 - lobe_weight) + (detail - 0.5) * mottle, 0, 1
    )

    colour = np.stack(
        [shadow[c] + (lit[c] - shadow[c]) * shade for c in range(3)], axis=-1
    ) * tone
    colour = colour * btone[..., None]
    colour = colour * (1 + (crease - 1) * np.clip((unioned - best) * 5.0, 0, 1))[..., None]
    colour = colour * (rim + (1 - rim) * np.clip(unioned * 3.2, 0, 1))[..., None]
    return colour, alpha


def to_output(colour, alpha, floor_linear=0.462):
    """Alpha-blends over the board floor in linear space, then encodes the way the frame is graded."""
    blended = colour * alpha[..., None] + floor_linear * (1 - alpha[..., None])
    return 255 * np.clip(blended, 0, 1) ** (1 / 2.2)


LIT = np.array([0.225, 0.196, 0.160])
SHADOW = np.array([0.026, 0.021, 0.017])

panels = []
print(f"{'case':>22} {'cover%':>7} {'meanL':>7} {'minL':>6} {'maxL':>6} {'lobeSD':>7} {'dLum':>6}")
for label, seed, lobes, erode, tear, opacity, tone in [
    ("core t=0.37", 7.3, 5, 0.00, 0.00, 1.00, 0.92),
    ("core t=0.50", 7.3, 5, 0.06, 0.05, 1.00, 0.92),
    ("core t=0.93", 7.3, 5, 0.26, 0.22, 0.86, 0.92),
    ("satellite", 19.1, 3, 0.00, 0.00, 1.00, 1.12),
    ("skirt", 24.7, 3, 0.10, 0.09, 1.00, 1.18),
]:
    colour, alpha = render(seed, lobes, erode, tear, opacity, LIT, SHADOW, tone)
    rgb = to_output(colour, alpha)
    lum = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]
    solid = alpha > 0.8
    cover = solid.mean() * 100
    if solid.any():
        inside = lum[solid]
        # the critic's statistic: spread measured over a lobe-sized window, worst case of several
        half = int(N * 0.13)
        worst = 0.0
        for _ in range(24):
            cy = np.random.randint(half, N - half)
            cx = np.random.randint(half, N - half)
            wa = alpha[cy - half:cy + half, cx - half:cx + half] > 0.8
            if wa.mean() < 0.85:
                continue
            worst = max(worst, lum[cy - half:cy + half, cx - half:cx + half][wa].std())
        print(f"{label:>22} {cover:7.2f} {inside.mean():7.1f} {inside.min():6.1f} "
              f"{inside.max():6.1f} {worst:7.1f} {inside.mean() - FLOOR_LUM:6.1f}")
    panels.append(Image.fromarray(np.uint8(rgb)).resize((300, 300)))

sheet = Image.new("RGB", (300 * len(panels), 300), (168, 184, 188))
for i, panel in enumerate(panels):
    sheet.paste(panel, (i * 300, 0))
sheet.save("_r2_smoke_lobes.png")

# occlusion check: a tile seam under the densest part of the mass
colour, alpha = render(7.3, 5, 0.0, 0.0, 1.0, LIT, SHADOW, 0.92)
seam_floor = np.array([0.462, 0.462 * 0.55])  # clean floor vs a seam, linear
under = [
    255 * np.clip(colour[N // 2, N // 2] * alpha[N // 2, N // 2] + f * (1 - alpha[N // 2, N // 2]),
                  0, 1) ** (1 / 2.2)
    for f in seam_floor
]
clean = [255 * np.clip(f, 0, 1) ** (1 / 2.2) for f in seam_floor]


def lum1(v):
    v = np.atleast_1d(v)
    return float(0.2126 * v[0] + 0.7152 * v[min(1, len(v) - 1)] + 0.0722 * v[min(2, len(v) - 1)])


print()
print(f"clean floor seam contrast : {abs(clean[0] - clean[1]):.1f}")
print(f"seam contrast under core  : {abs(lum1(under[0]) - lum1(under[1])):.1f}")
print("saved _r2_smoke_lobes.png")
