"""Transliteration of Assets/Editor/BattlePlanFXMaskBaker.cs used to preview the mask shapes
without an Editor round-trip. Renders each mask over a mid-grey checker so alpha is visible.

Not part of the build. Kept because it is how the baker's constants were checked, and re-running
it is cheaper than a Unity import cycle when one of them is retuned.
"""

import math
import struct
import zlib
import os

OUT = os.path.join(os.path.dirname(__file__), "preview")

# --- constants, mirrored from the C# block -------------------------------------------------
GLOW_SIZE, GLOW_FALLOFF, GLOW_PEAK = 256, 1.9, 0.94
STAR_SIZE = 512
STAR_ARM, STAR_DIAG, STAR_CORE = 0.5, 0.27, 0.13
STAR_SHARP, STAR_ARM_FALLOFF, STAR_CORE_FALLOFF = 6.0, 2.3, 2.4
STREAK_W, STREAK_H = 256, 64
STREAK_HEAD, STREAK_TAIL, STREAK_CURVE = 0.26, 0.03, 2.1
STREAK_EDGE, STREAK_TAPER, STREAK_CAP, STREAK_NOISE = 0.85, 1.35, 0.09, 0.12
PUFF_SIZE, PUFF_CHAOS, PUFF_SOFT, PUFF_VAR, PUFF_PEAK = 256, 0.22, 0.30, 0.34, 0.95
PUFF_SHAPE_P, PUFF_DETAIL_P = 4, 11
PUFF_BLOBS = [(0.00, 0.02, 0.30), (-0.16, -0.07, 0.22), (0.17, 0.05, 0.20),
              (0.04, 0.18, 0.17), (-0.09, 0.15, 0.14), (0.12, -0.16, 0.15)]
SCORCH_SIZE, SCORCH_R, SCORCH_RIM_VAR, SCORCH_RIM_DETAIL = 512, 0.42, 0.20, 0.075
SCORCH_SOFT, SCORCH_MOTTLE, SCORCH_PEAK = 0.05, 0.26, 0.94
SCORCH_GRAIN_LOW, SCORCH_GRAIN_HIGH = 0.30, 0.80
SCORCH_RIM_P, SCORCH_RIM_DETAIL_P, SCORCH_MOTTLE_P, SCORCH_GRAIN_P = 3, 8, 9, 26
S_SCORCH_RIM_DETAIL = 157
SLASH_SIZE, SLASH_R, SLASH_SWEEP = 256, 0.42, 104.0
SLASH_PEAK_POS, SLASH_MAX_W, SLASH_EDGE = 0.34, 0.052, 1.5
NOISE_SIZE, NOISE_P, NOISE_OCT, NOISE_CONTRAST = 256, 4, 4, 1.15
S_STREAK, S_PUFF_SHAPE, S_PUFF_DETAIL = 21, 53, 97
S_SCORCH_RIM, S_SCORCH_MOTTLE, S_SCORCH_GRAIN, S_NOISE = 131, 179, 223, 271


def saturate(x):
    return 0.0 if x < 0.0 else (1.0 if x > 1.0 else x)


def smoothstep(e0, e1, x):
    if e0 == e1:
        return 0.0 if x < e0 else 1.0
    t = saturate((x - e0) / (e1 - e0))
    return t * t * (3.0 - 2.0 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def inv_lerp(a, b, x):
    return 0.0 if a == b else saturate((x - a) / (b - a))


def hash01(x, y, seed):
    h = (x * 374761393 + y * 668265263 + seed * 1274126177) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    h ^= h >> 16
    return (h & 0xFFFFFF) / float(0xFFFFFF)


def value_noise(x, y, period, seed):
    x0, y0 = math.floor(x), math.floor(y)
    fx, fy = smoothstep(0, 1, x - x0), smoothstep(0, 1, y - y0)
    xa, xb = x0 % period, (x0 + 1) % period
    ya, yb = y0 % period, (y0 + 1) % period
    top = lerp(hash01(xa, ya, seed), hash01(xb, ya, seed), fx)
    bot = lerp(hash01(xa, yb, seed), hash01(xb, yb, seed), fx)
    return lerp(top, bot, fy)


def fbm(u, v, base_period, octaves, seed):
    total = amp = 0.0
    acc = 0.0
    amp = 1.0
    period = max(1, base_period)
    for o in range(octaves):
        acc += value_noise(u * period, v * period, period, seed + o * 101) * amp
        total += amp
        amp *= 0.5
        period *= 2
    return acc / total if total > 0 else 0.0


def radius(u, v):
    return math.hypot(u - 0.5, v - 0.5)


def arm(r, reach, falloff):
    return 0.0 if reach <= 1e-4 else saturate(1.0 - r / reach) ** falloff


# --- masks ---------------------------------------------------------------------------------
def glow(u, v):
    return smoothstep(1, 0, radius(u, v) * 2) ** GLOW_FALLOFF * GLOW_PEAK


def star(u, v):
    dx, dy = u - 0.5, v - 0.5
    r, a = math.hypot(dx, dy), math.atan2(dy, dx)
    axis = abs(math.cos(2 * a)) ** STAR_SHARP
    diag = abs(math.cos(2 * a - math.pi * 0.5)) ** STAR_SHARP
    return max(arm(r, STAR_ARM * axis, STAR_ARM_FALLOFF),
               arm(r, STAR_DIAG * diag, STAR_ARM_FALLOFF),
               arm(r, STAR_CORE, STAR_CORE_FALLOFF))


def streak(u, v):
    across_c = abs(v - 0.5) * 2
    half = lerp(STREAK_TAIL, STREAK_HEAD, u ** STREAK_CURVE)
    across = saturate(1 - across_c / max(half, 1e-4)) ** STREAK_EDGE
    taper = u ** STREAK_TAPER
    cap = smoothstep(0, 1, inv_lerp(1.0, 1.0 - STREAK_CAP, u))
    grain = fbm(u * 3, v, 8, 2, S_STREAK)
    return across * taper * cap * lerp(1 - STREAK_NOISE, 1, grain)


def puff(u, v):
    cov = 0.0
    for bx, by, br in PUFF_BLOBS:
        cov = max(cov, 1 - math.hypot(u - 0.5 - bx, v - 0.5 - by) / br)
    edge = cov + (fbm(u, v, PUFF_SHAPE_P, 3, S_PUFF_SHAPE) - 0.5) * PUFF_CHAOS
    a = smoothstep(0, PUFF_SOFT, edge)
    a *= lerp(1 - PUFF_VAR, 1, fbm(u, v, PUFF_DETAIL_P, 3, S_PUFF_DETAIL))
    a *= smoothstep(1, 0.82, radius(u, v) * 2)
    return a * PUFF_PEAK


def scorch(u, v):
    r = radius(u, v)
    rim = fbm(u, v, SCORCH_RIM_P, 2, S_SCORCH_RIM)
    rim_detail = fbm(u, v, SCORCH_RIM_DETAIL_P, 2, S_SCORCH_RIM_DETAIL)
    edge_r = SCORCH_R * (1 + (rim - 0.5) * 2 * SCORCH_RIM_VAR
                         + (rim_detail - 0.5) * 2 * SCORCH_RIM_DETAIL)
    a = smoothstep(edge_r, edge_r - SCORCH_SOFT * SCORCH_R, r)
    near = saturate(r / max(edge_r, 1e-4)) ** 4
    grain = smoothstep(SCORCH_GRAIN_LOW, SCORCH_GRAIN_HIGH,
                       fbm(u, v, SCORCH_GRAIN_P, 3, S_SCORCH_GRAIN))
    a *= lerp(1, grain, near)
    a *= lerp(1 - SCORCH_MOTTLE, 1, fbm(u, v, SCORCH_MOTTLE_P, 3, S_SCORCH_MOTTLE))
    return a * SCORCH_PEAK


HALF_SWEEP = math.radians(SLASH_SWEEP) * 0.5


def slash(u, v):
    dx = u - 0.5 + SLASH_R * 0.62
    dy = v - 0.5
    r, a = math.hypot(dx, dy), math.atan2(dy, dx)
    if abs(a) > HALF_SWEEP:
        return 0.0
    along = inv_lerp(-HALF_SWEEP, HALF_SWEEP, a)
    taper = inv_lerp(0, SLASH_PEAK_POS, along) if along < SLASH_PEAK_POS \
        else inv_lerp(1, SLASH_PEAK_POS, along)
    half = SLASH_MAX_W * smoothstep(0, 1, taper)
    if half <= 1e-5:
        return 0.0
    return saturate(1 - abs(r - SLASH_R) / half) ** SLASH_EDGE


def noise(u, v):
    return saturate((fbm(u, v, NOISE_P, NOISE_OCT, S_NOISE) - 0.5) * NOISE_CONTRAST + 0.5)


# --- output --------------------------------------------------------------------------------
def write_png(path, w, h, rows):
    raw = b"".join(b"\x00" + bytes(r) for r in rows)
    def chunk(t, d):
        c = t + d
        return struct.pack(">I", len(d)) + c + struct.pack(">I", zlib.crc32(c))
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))
    open(path, "wb").write(png)


def render(name, w, h, fn, is_noise=False):
    rows = []
    for y in range(h):
        row = bytearray()
        for x in range(w):
            u, v = (x + 0.5) / w, (y + 0.5) / h
            val = fn(u, v)
            if is_noise:
                g = int(saturate(val) * 255)
                row += bytes((g, g, g))
            else:
                # white mask composited over a checker, so alpha is what you see
                bg = 40 if ((x // 16) + (y // 16)) % 2 == 0 else 70
                a = saturate(val)
                g = int(bg * (1 - a) + 255 * a)
                row += bytes((g, g, g))
        rows.append(row)
    os.makedirs(OUT, exist_ok=True)
    write_png(os.path.join(OUT, name + ".png"), w, h, rows)
    peak = max(fn((x + 0.5) / w, (y + 0.5) / h)
               for y in range(0, h, 2) for x in range(0, w, 2))
    print(f"{name:16s} {w}x{h}  peak alpha {peak:.3f}")


if __name__ == "__main__":
    render("RadialGlow", GLOW_SIZE, GLOW_SIZE, glow)
    render("MuzzleStar", STAR_SIZE, STAR_SIZE, star)
    render("SparkStreak", STREAK_W, STREAK_H, streak)
    render("SmokePuff", PUFF_SIZE, PUFF_SIZE, puff)
    render("Scorch", SCORCH_SIZE, SCORCH_SIZE, scorch)
    render("Slash", SLASH_SIZE, SLASH_SIZE, slash)
    render("Noise", NOISE_SIZE, NOISE_SIZE, noise, is_noise=True)
