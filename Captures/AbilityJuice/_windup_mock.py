"""Software rasteriser for the rebuilt wind-up, so the geometry and contrast can be checked before
the editor ever sees it.

Reimplements BP_WindupTelegraph.shader pixel for pixel over a clean pre-fire frame from the r1
capture, using the studio camera's real pose (73 degree pitch, 60 degree FOV, 14 units out). It is
an approximation in one respect only: it composites in sRGB-decoded display space rather than
pre-tonemap scene-linear, so absolute values will drift a little. Geometry, edge hardness, coverage
and the shape of the countdown are exact.
"""

import math
from pathlib import Path

import numpy as np
from PIL import Image

HERE = Path(__file__).parent
SHOT = HERE / "shots" / "r1" / "windup"
OUT = HERE / "_r2_mock_strip.png"

SIZE = 1024
CELL = 2.7
DURATION = 1.1
RADIUS = 4.3
PITCH = 73.0
FOV = 60.0
DISTANCE = 14.0
TARGET = np.array([8 * CELL, 0.0, 5 * CELL])
PLATE_Y = 0.056

OPENING_TINT = np.array([0.66, 0.26, 0.075])
LOADED_TINT = np.array([0.58, 0.22, 0.055])
BULLSEYE_MUL = 0.66
COLLAPSE = 1.85
COLLAPSE_SNAP = 2.9
BORDER_W = 0.15
KEY_W = 0.06
SEAM_W = 0.03
BULL_W = 0.10
RING_START = 2.2 * CELL
RING_EASE = 0.6
RING_FILL = 1.35
RING_RIM = 0.12
RING_SHADE_W = 0.30
RING_SHADE_MUL = 0.68
REVEAL_SECONDS = 0.14
ARM_FLASH = 0.085
CUT_SECONDS = 0.055


def srgb_to_linear(a):
    a = a / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(a):
    a = np.clip(a, 0, None)
    out = np.where(a <= 0.0031308, a * 12.92, 1.055 * np.power(a, 1 / 2.4) - 0.055)
    return np.clip(out, 0, 1) * 255.0


def board_coordinates():
    pitch = math.radians(PITCH)
    forward = np.array([0.0, -math.sin(pitch), math.cos(pitch)])
    up = np.array([0.0, math.cos(pitch), math.sin(pitch)])
    right = np.array([1.0, 0.0, 0.0])
    cam = TARGET - forward * DISTANCE

    half = math.tan(math.radians(FOV / 2))
    ndc = (np.arange(SIZE) + 0.5) / SIZE * 2.0 - 1.0
    nx, ny = np.meshgrid(ndc, -ndc)

    d = (
        forward[None, None, :]
        + right[None, None, :] * (nx * half)[..., None]
        + up[None, None, :] * (ny * half)[..., None]
    )
    d /= np.linalg.norm(d, axis=-1, keepdims=True)

    t = (PLATE_Y - cam[1]) / d[..., 1]
    hit = cam[None, None, :] + d * t[..., None]
    behind = t <= 0
    bx = np.where(behind, 1e6, hit[..., 0] - TARGET[0])
    bz = np.where(behind, 1e6, hit[..., 2] - TARGET[2])
    return bx, bz


def segment_distance(x, z):
    return np.hypot(x, z)


def cell_live(ix, iz, front, speed, reveal):
    cx = ix * CELL
    cz = iz * CELL
    inside = segment_distance(cx, cz) <= RADIUS
    age = (front - (cx * reveal[0] + cz * reveal[1])) / speed
    return inside & (age > 0.0), age


def sample(bx, bz, pixel, t):
    p = min(t / DURATION, 1.0)
    pressure = p**1.7
    strength = 0.94 + (1.0 - 0.94) * min(p * 3.0, 1.0)
    tint_rgb = OPENING_TINT + (LOADED_TINT - OPENING_TINT) * pressure
    beat = (0.5 + 0.5 * math.sin(min(p, 1.0) * 9.0 * math.pi * 2)) ** 3
    border_w = BORDER_W * (1 + 0.5 * beat * pressure)
    seam_alpha = 0.35 + (0.62 - 0.35) * pressure

    reveal = np.array([1.0, 0.0])
    plate_half = RADIUS + CELL * 0.5
    speed = (2 * plate_half + CELL) / REVEAL_SECONDS
    front = -plate_half - 0.01 + speed * min(t, DURATION)

    cut = max(0.0, (t - DURATION)) / CUT_SECONDS
    firing = t > DURATION
    if firing:
        strength = 0.0
        border_w = -1.0
        key_w = -1.0
        bull_w = -1.0
        seam_alpha = 0.0
        ring_r = 0.0
        thick = COLLAPSE + (COLLAPSE_SNAP - COLLAPSE) * min(cut, 1.0)
        ring_a = max(0.0, 1.0 - min(cut, 1.0) ** 2)
    else:
        key_w = KEY_W
        bull_w = BULL_W
        ring_r = RING_START * max(0.0, 1.0 - p) ** RING_EASE
        thick = max(
            0.22 + (0.46 - 0.22) * p**1.5,
            COLLAPSE * min(max((RING_FILL - ring_r) / RING_FILL, 0.0), 1.0),
        )
        ring_a = 1.0

    ix = np.floor(bx / CELL + 0.5)
    iz = np.floor(bz / CELL + 0.5)
    fx = bx / CELL - ix
    fz = bz / CELL - iz

    to_r = (0.5 - fx) * CELL
    to_l = (fx + 0.5) * CELL
    to_u = (0.5 - fz) * CELL
    to_d = (fz + 0.5) * CELL

    me, age = cell_live(ix, iz, front, speed, reveal)
    nbr = [
        (cell_live(ix + 1, iz, front, speed, reveal)[0], to_r),
        (cell_live(ix - 1, iz, front, speed, reveal)[0], to_l),
        (cell_live(ix, iz + 1, front, speed, reveal)[0], to_u),
        (cell_live(ix, iz - 1, front, speed, reveal)[0], to_d),
    ]
    outer = np.full_like(bx, 1e6)
    inner = np.full_like(bx, 1e6)
    for live, dist in nbr:
        outer = np.where(live, outer, np.minimum(outer, dist))
        inner = np.where(live, np.minimum(inner, dist), inner)

    inside = np.where(me, np.clip(outer / pixel + 0.5, 0, 1), 0.0)
    border = np.where(me, np.clip((border_w - outer) / pixel + 0.5, 0, 1) * inside, 0.0)
    seam = np.where(me, np.clip((SEAM_W - inner) / pixel + 0.5, 0, 1) * inside, 0.0)
    seam = np.minimum(seam, 1.0 - border)
    flash = np.where(me, np.clip(1.0 - age / ARM_FLASH, 0, 1), 0.0)
    jitter = np.where(me, np.modf(np.sin(ix * 37.13 + iz * 71.97) * 4371.31)[0], 0.0)
    bull = np.where(me & (np.abs(ix) < 0.5) & (np.abs(iz) < 0.5), 1.0, 0.0)
    nearest = np.minimum(np.minimum(to_r, to_l), np.minimum(to_u, to_d))
    bull_edge = np.clip((bull_w - nearest) / pixel + 0.5, 0, 1) * bull * inside
    key = np.where(me, 0.0, np.clip((key_w - inner) / pixel + 0.5, 0, 1))

    radius = np.hypot(bx, bz)
    band = np.abs(radius - ring_r) - thick * 0.5
    core = np.clip(-band / pixel + 0.5, 0, 1)
    rim = np.clip(np.clip((RING_RIM - band) / pixel + 0.5, 0, 1) - core, 0, 1)
    shade = np.clip(1.0 - (band - RING_RIM) / RING_SHADE_W, 0, 1)

    depth = inside * strength * (1.0 - 0.09 * jitter)
    mul = 1.0 + (tint_rgb[None, None, :] - 1.0) * depth[..., None]
    mul = mul * (1.0 + (BULLSEYE_MUL - 1.0) * (bull * depth))[..., None]
    mul = mul * (1.0 - flash)[..., None] + flash[..., None]
    mul = mul * (1.0 + (RING_SHADE_MUL - 1.0) * (shade**2 * ring_a))[..., None]

    ring_gain = 1.0 + 1.6 * min(max((0.9 - ring_r) / 0.9, 0.0), 1.0)
    if firing:
        ring_gain = 2.6 + (0.6 - 2.6) * min(cut, 1.0)

    return mul, dict(
        seam=seam * seam_alpha,
        bull_edge=bull_edge,
        border=border,
        key=key,
        flash=flash * inside * 0.9,
        rim=rim * ring_a,
        core=core * ring_a,
        gain=ring_gain,
    )


def over(dst_rgb, dst_a, colour, alpha):
    alpha = np.clip(alpha, 0, 1)[..., None]
    return colour[None, None, :] * alpha + dst_rgb * (1 - alpha), None


ALARM = srgb_to_linear(np.array([255.0, 138.0, 8.0]))
KEY = srgb_to_linear(np.array([24.0, 12.0, 6.0]))
SEAM = srgb_to_linear(np.array([150.0, 60.0, 18.0]))


def render(base_linear, floor_mask, bx, bz, pixel, t):
    mul, ink = sample(bx, bz, pixel, t)
    out = base_linear * np.where(floor_mask[..., None], mul, 1.0)

    layers = [
        (SEAM, ink["seam"]),
        (ALARM, ink["bull_edge"]),
        (ALARM, ink["border"]),
        (KEY, ink["key"]),
        (ALARM, ink["flash"]),
        (KEY, ink["rim"]),
        (ALARM * ink["gain"], ink["core"]),
    ]
    for colour, alpha in layers:
        a = np.clip(alpha, 0, 1)[..., None] * floor_mask[..., None]
        out = colour[None, None, :] * a + out * (1 - a)
    return out


def main():
    base = np.asarray(Image.open(SHOT / "f0000.jpg").convert("RGB")).astype(np.float32)
    base_linear = srgb_to_linear(base)

    luma = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]
    # Everything the decal would be occluded by — crates, units, their shadows — is far darker than
    # the floor, so a luminance gate stands in for a depth test.
    floor_mask = luma > 132

    bx, bz = board_coordinates()
    pixel = np.maximum(
        np.abs(np.gradient(bx, axis=1)), np.abs(np.gradient(bz, axis=0))
    )
    pixel = np.maximum(pixel, 1e-4)
    print(f"world units per pixel at centre: {pixel[512, 512]:.4f} -> {1 / pixel[512, 512]:.1f} px/unit")

    panels = []
    for t in (0.06, 0.35, 0.75, 1.02, 1.10, 1.14, 1.40):
        frame = render(base_linear, floor_mask, bx, bz, pixel, t)
        srgb = linear_to_srgb(frame)
        report(t, srgb, base)
        panels.append(srgb.astype(np.uint8))

    strip = np.concatenate([np.asarray(Image.fromarray(p).resize((512, 512))) for p in panels], axis=1)
    Image.fromarray(strip).save(OUT)
    print(f"\nwrote {OUT}")


def report(t, srgb, base):
    def lum(a):
        return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]

    def sat(a):
        mx = a.max(axis=-1)
        mn = a.min(axis=-1)
        return np.where(mx > 1e-3, (mx - mn) / np.maximum(mx, 1e-3), 0.0)

    changed = np.abs(srgb - base).max(axis=-1) > 6
    l = lum(srgb)
    s = sat(srgb)
    ceiling = np.percentile(lum(base), 97)
    bright = (l > ceiling + 6) & changed
    dark = (l < 140) & changed

    ys, xs = np.where(changed)
    bbox = f"{xs.max() - xs.min():4d}x{ys.max() - ys.min():4d}" if changed.any() else "   none"
    ys2, xs2 = np.where(bright)
    bbright = f"{xs2.max() - xs2.min():4d}" if bright.any() else "   0"

    print(
        f"t {t:+.2f}  changed {changed.mean() * 100:5.2f}%  bbox {bbox}  "
        f"bright {bright.mean() * 100:5.2f}% (bbox {bbright}px)  "
        f"dark {dark.mean() * 100:5.2f}%  "
        f"mark luma {l[changed].mean() if changed.any() else 0:6.1f}  "
        f"mark sat {s[changed].mean() if changed.any() else 0:.3f}"
    )


main()
