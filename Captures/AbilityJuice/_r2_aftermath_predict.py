"""Projects the round-2 aftermath timeline through the studio camera and reports the numbers the
critic measured: footprint in cells, centroid drift, bbox growth, coverage peak, dissipation time,
and where juice.py's strip picker will cut."""

import math

import numpy as np

# --- studio camera (AbilityFilmStudio: 73 degree pitch, FOV 60, distance 12, square frame) -------
PITCH = math.radians(73.0)
DIST = 12.0
FOV = math.radians(60.0)
TANH = math.tan(FOV / 2)
CELL = 2.7
RADIUS = 3.0

FWD = np.array([0.0, -math.sin(PITCH), math.cos(PITCH)])
UP = np.array([0.0, math.cos(PITCH), math.sin(PITCH)])
RIGHT = np.array([1.0, 0.0, 0.0])
CAM = -FWD * DIST

NDC_PER_CELL = CELL / (DIST * TANH)  # ground cell width at the look-at point


def project(p):
    rel = np.asarray(p, dtype=float) - CAM
    depth = float(rel @ FWD)
    return (
        float(rel @ RIGHT) / (depth * TANH),
        float(rel @ UP) / (depth * TANH),
        depth,
    )


# --- curves, mirrored from Aftermath.cs ----------------------------------------------------------
def grow_curve(p, grow):
    burst = 1 - (1 - min(max(p / 0.3, 0.0), 1.0)) ** 2.2
    swell = min(max((p - 0.3) / 0.7, 0.0), 1.0)
    return (0.28 + 0.72 * burst) * (1 + (grow - 1) * (1 - (1 - swell) ** 1.6))


def smoothstep(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * (3 - 2 * x)


def fade(p, hold_end):
    if p < 0.05:
        return p / 0.05
    if p < hold_end:
        return 1.0
    tail = (p - hold_end) / (1 - hold_end)
    return 1 - smoothstep(tail) * tail


def erode_amount(p, start, mx):
    if p <= start:
        return 0.0
    return mx * ((p - start) / (1 - start)) ** 1.25


def floor_height(diameter):
    return 0.22 + diameter * 0.20


# size, ox, oz, rise, lean, outward, delay, life, grow, holdEnd, erodeStart, erodeMax, tone, lobes
MASSES = [
    (1.52, -0.06, 0.04, 1.00, 1.00, 0.00, 0.00, 1.42, 1.14, 0.34, 0.28, 0.62, 0.92),
    (0.84, 0.42, -0.23, 0.86, 0.78, 0.07, 0.05, 1.20, 1.28, 0.30, 0.24, 0.70, 1.00),
    (0.64, -0.38, 0.32, 1.12, 0.62, 0.09, 0.11, 1.06, 1.34, 0.30, 0.22, 0.72, 1.08),
    (0.50, 0.20, 0.58, 1.22, 0.55, 0.13, 0.18, 0.90, 1.40, 0.26, 0.20, 0.80, 1.12),
    (0.38, -0.62, -0.16, 0.74, 0.40, 0.16, 0.13, 0.82, 1.44, 0.26, 0.18, 0.84, 0.96),
    (0.31, 0.66, 0.30, 0.95, 0.30, 0.18, 0.24, 0.70, 1.44, 0.24, 0.18, 0.86, 1.16),
    (0.28, -0.16, -0.64, 1.30, 0.66, 0.17, 0.30, 0.64, 1.44, 0.24, 0.16, 0.88, 1.04),
    (0.78, -0.68, 0.20, 0.10, 0.06, 0.32, 0.00, 0.68, 1.62, 0.22, 0.16, 0.90, 1.18),
    (0.62, 0.72, -0.34, 0.08, 0.05, 0.34, 0.03, 0.60, 1.62, 0.22, 0.14, 0.92, 1.22),
    (0.52, 0.10, 0.78, 0.12, 0.05, 0.32, 0.06, 0.56, 1.70, 0.20, 0.14, 0.92, 1.26),
]

RISE = 0.86 * RADIUS
LEAN = 0.96 * RADIUS
LEAN_ANGLE = 0.40  # mid of the +-0.55 rad band the code samples
LEAN_DIR = np.array([math.sin(LEAN_ANGLE), 0.0, math.cos(LEAN_ANGLE)])

MARK_SCALE, MARK_RISE, MARK_HOLD, MARK_FADE = 2.65, 0.22, 1.73, 0.53
MARK_SPAN = 0.78  # measured fraction of the decal quad the blotch actually covers
COAL_SCALE, COAL_PEAK, COAL_OUT = 1.25, 0.40, 1.25

# Output luminance of each layer, from the round-1 calibration (floor measures 181).
FLOOR_LUM = 181.0
SMOKE_LUM = 85.0  # area mean of a lit-to-shadow lambert on the new palette
MARK_CORE_DELTA = -68.0


def mark_opacity(t):
    if t < MARK_RISE:
        return 0.82 * (1 - (1 - t / MARK_RISE) ** 2.2)
    if t < MARK_RISE + MARK_HOLD:
        return 0.82
    f = (t - MARK_RISE - MARK_HOLD) / MARK_FADE
    return 0.82 * (1 - smoothstep(f)) if f < 1 else 0.0


def coal_envelope(t):
    if t < COAL_PEAK:
        return 0.2 + 0.8 * smoothstep(t / COAL_PEAK)
    cool = (t - COAL_PEAK) / (COAL_OUT - COAL_PEAK)
    return max(0.0, 1 - cool) ** 1.7


# --- rasterise -----------------------------------------------------------------------------------
N = 384
axis = np.linspace(-1, 1, N)
gx, gy = np.meshgrid(axis, axis)


def frame(t):
    """Returns (smoke alpha field, mark alpha field, visible coal fraction)."""
    smoke = np.zeros((N, N))
    for spec in MASSES:
        (size_f, ox, oz, rise_s, lean_s, outward, delay, life, grow, hold, e0, emax, _tone) = spec
        if t < delay:
            continue
        p = min(1.0, (t - delay) / life)
        if p >= 1.0:
            continue

        climb = 1 - (1 - p) ** 2
        spread = 1 - (1 - p) ** 2.6
        scale = grow_curve(p, grow)
        diameter = size_f * RADIUS * scale

        anchor = np.array([ox * RADIUS, 0.0, oz * RADIUS])
        out_dir = anchor / np.linalg.norm(anchor) if np.linalg.norm(anchor) > 1e-4 else LEAN_DIR
        pos = anchor + out_dir * (outward * RADIUS * spread) + LEAN_DIR * (LEAN * lean_s * climb)
        pos[1] = floor_height(diameter) + RISE * rise_s * climb

        nx, ny, depth = project(pos)
        erode = erode_amount(p, e0, emax)
        # erosion walks the coverage cut inwards through the lobe field
        screen_r = (diameter / 2) * max(0.0, 1 - erode * 0.95) / (depth * TANH)
        alpha = fade(p, hold)
        disc = ((gx - nx) ** 2 + (gy - ny) ** 2) < screen_r**2
        smoke = np.maximum(smoke, disc * alpha)

    mark = np.zeros((N, N))
    mo = mark_opacity(t)
    if mo > 0.01:
        mr = (RADIUS * MARK_SCALE * MARK_SPAN / 2) / (DIST * TANH)
        mark = (((gx - 0.05) ** 2 + (gy + 0.03) ** 2) < mr**2) * mo

    coal_r = (RADIUS * COAL_SCALE / 2) / (DIST * TANH)
    coal_disc = ((gx - 0.05) ** 2 + (gy + 0.03) ** 2) < coal_r**2
    visible = np.logical_and(coal_disc, smoke < 0.35)
    coal = coal_envelope(t) * (visible.sum() / max(1, coal_disc.sum())) * 0.22
    return smoke, mark, coal


print(f"{'t':>6} {'cov%':>6} {'bboxW':>6} {'bboxH':>6} {'cx':>6} {'cy':>6} "
      f"{'drift':>6} {'change':>7} {'flash':>6}")
rows = []
for i in range(79):
    t = i / 30.0
    smoke, mark, coal = frame(t)
    mask = smoke > 0.2
    cov = mask.mean() * 100
    if mask.any():
        ys, xs = np.where(mask)
        w = (axis[xs.max()] - axis[xs.min()]) / NDC_PER_CELL
        h = (axis[ys.max()] - axis[ys.min()]) / NDC_PER_CELL
        cx = axis[xs].mean() / NDC_PER_CELL
        cy = axis[ys].mean() / NDC_PER_CELL
    else:
        w = h = cx = cy = 0.0
    change = (
        smoke * (FLOOR_LUM - SMOKE_LUM) + (1 - smoke) * mark * abs(MARK_CORE_DELTA)
    ).mean() / 255.0
    rows.append((t, cov, w, h, cx, cy, change, coal))

base = next(r for r in rows if r[1] > 1)
for t, cov, w, h, cx, cy, change, coal in rows:
    if t > 2.62:
        break
    drift = math.hypot(cx - base[4], cy - base[5]) if cov > 1 else 0.0
    print(f"{t:6.3f} {cov:6.2f} {w:6.2f} {h:6.2f} {cx:6.2f} {cy:6.2f} "
          f"{drift:6.2f} {change:7.4f} {coal:6.3f}")


def unit(v):
    v = np.asarray(v)
    span = v.max() - v.min()
    return (v - v.min()) / span if span > 1e-9 else np.zeros_like(v)


score = 0.6 * unit([r[7] for r in rows]) + 0.4 * unit([r[6] for r in rows])
peak = int(np.argmax(score))
peak_t = rows[peak][0]
print()
print(f"predicted strip cut: panel1 t={peak_t - 0.1333:+.3f}  "
      f"panel2 t={peak_t:+.3f}  panel3 t={peak_t + 0.4333:+.3f}")
cov = [r[1] for r in rows]
print(f"coverage peak t={rows[int(np.argmax(cov))][0]:.3f}  max={max(cov):.2f}%")
alive = [r[0] for r in rows if r[1] > 0.15]
print(f"smoke gone by t={max(alive) + 1 / 30:.2f}s   mark gone by t="
      f"{MARK_RISE + MARK_HOLD + MARK_FADE:.2f}s")
peak_i = int(np.argmax(cov))
print(f"bbox at coverage peak: {rows[peak_i][2]:.2f} x {rows[peak_i][3]:.2f} cells")
late = [r for r in rows if 1.0 <= r[0] <= 1.2]
if late:
    print(f"bbox at t=1.1: {late[len(late) // 2][2]:.2f} x {late[len(late) // 2][3]:.2f} cells "
          f"({100 * late[len(late) // 2][2] / rows[peak_i][2] - 100:+.0f}% width)")
