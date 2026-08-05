"""Round-4 death builder: render BP_DeathMark offline and measure it the way the critics do.

No Unity lease, so the only honest way to check a hole that has to land under luminance 35, carry
saturated colour over area, clip in one hue and never repeat a silhouette is to run the fragment
shader on the CPU against the same grade the capture goes through.
"""
import numpy as np
from PIL import Image

from _b4_hue import grade

FRAME = 1024
CELL_PX = 218.0                     # one 2.7-unit cell at the death shot's 11-unit camera
UNIT_PX = CELL_PX / 2.7             # world units to screen pixels, horizontally
DEPTH_SQUASH = 0.956                # sin(73 deg): a ground depth axis on screen
HEIGHT_SQUASH = 0.292               # cos(73 deg): a world height on screen

CELL = 2.7
HOLE_CELLS = 1.35
HOLE_PAD = 2.15
HOLE_RADIUS_CAP = 0.52
CRACK_LENGTH = 0.90

SINK_S = 0.17
SUBMERGED_S = 0.10
SWALLOW_S = 0.15
OPEN_S = SINK_S
PEAK_S = OPEN_S + SUBMERGED_S
CHURN_S = PEAK_S + SWALLOW_S
CLOSE_S = 0.74
LIFE_S = PEAK_S + CLOSE_S
PHASE_RATE = 22.0
CLOSE_GAIN = 1.34
CLOSE_SHAPE = 0.85
ERODE_SHAPE = 0.72
HOLD_SHRINK = 0.97
HOLD_GLOW = 0.93
BITE_DRIFT = 0.25
CLOSE_SPIN = 4.2

SINK_DEPTH = 0.46
CRUSH_HEIGHT = 0.42
CRUSH_SPREAD = 1.22
SWALLOW_DEPTH = 1.05
SWALLOW_HEIGHT = 0.42

HOLE_FLOOR = np.array([0.0060, 0.0092, 0.0115])
HOLE_WALL = np.array([0.0115, 0.0180, 0.0225])
HOLE_LIP = np.array([0.0300, 0.0400, 0.0470])
VOID_WASH = np.array([0.020, 0.86, 2.15])
VOID_CORE = np.array([0.030, 1.85, 5.60])
VOID_GLINT = np.array([3.9, 4.3, 4.7])
BOARD = np.array([0.44, 0.50, 0.54])

RAG = 0.36
RAG_COUNT = 3.1
GRAIN = 0.45
GRAIN_SCALE = 13.0
SEED = 7.3
PHASE0 = 4.1
CLOSE_ANGLE = 1.05


def lerp(a, b, t):
    return a + (b - a) * np.clip(t, 0, 1)


def inv_lerp(a, b, t):
    return np.clip((t - a) / (b - a), 0, 1)


def hash1(n):
    return np.modf(np.sin(n) * 43758.5453)[0] % 1.0


def glow_at(t):
    if t <= 0.045:
        return 0.0
    if t < OPEN_S:
        return inv_lerp(0.045, OPEN_S, t) ** 1.3
    if t < PEAK_S:
        return lerp(1.0, HOLD_GLOW, inv_lerp(OPEN_S, PEAK_S, t))
    return lerp(HOLD_GLOW, 0.0, inv_lerp(PEAK_S, PEAK_S + CLOSE_S * 0.72, t) ** 0.85)


def crack_at(t):
    if t < 0.07:
        return lerp(0.55, 1.0, inv_lerp(0.0, 0.07, t))
    if t < PEAK_S:
        return 1.0
    return 1.0 - inv_lerp(PEAK_S, PEAK_S + CLOSE_S, t)


def crack_glow_at(t):
    if t < 0.05:
        return 0.7
    if t < 0.11:
        return lerp(0.7, 0.35, inv_lerp(0.05, 0.11, t))
    if t < OPEN_S:
        return lerp(0.35, 1.0, inv_lerp(0.11, OPEN_S, t))
    if t < PEAK_S:
        return 1.0
    return lerp(1.0, 0.0, inv_lerp(PEAK_S, PEAK_S + CLOSE_S * 0.8, t))


def hole_state(t):
    if t < OPEN_S:
        diameter = HOLE_CELLS * CELL * (t / OPEN_S) ** 0.85
        close, erode = 0.0, -0.7
    elif t < PEAK_S:
        held = (t - OPEN_S) / (PEAK_S - OPEN_S)
        diameter = HOLE_CELLS * CELL * lerp(1.0, HOLD_SHRINK, held)
        close, erode = 0.0, -0.7
    else:
        closing = np.clip((t - PEAK_S) / CLOSE_S, 0, 1)
        diameter = HOLE_CELLS * CELL * HOLD_SHRINK
        close = CLOSE_GAIN * closing ** CLOSE_SHAPE
        erode = lerp(-0.7, 1.25, closing ** ERODE_SHAPE)
    quad = HOLE_CELLS * CELL * HOLE_PAD
    return (min(diameter / quad, HOLE_RADIUS_CAP), close, erode,
            PHASE0 + t * PHASE_RATE, glow_at(t), crack_at(t), crack_glow_at(t))


def pool_distance(p, at, stretch, span, wobble, seed):
    offset = (p - at) / stretch
    reach = np.linalg.norm(offset, axis=-1)
    around = np.arctan2(offset[..., 1], offset[..., 0])
    bound = span * (1.0 + wobble * np.sin(around * 3.0 + seed)
                    + wobble * 0.45 * np.sin(around * 5.0 - seed * 1.7))
    return reach / np.maximum(bound, 1e-5)


def render_hole(p, aa, radius, close, erode, phase, glow, crack, crack_glow):
    """The BP_DeathMark fragment, vectorised. Returns linear rgb and alpha."""
    span = np.linalg.norm(p, axis=-1)
    r = span * 2.0
    angle = np.arctan2(p[..., 1], p[..., 0])

    rag = (np.sin(angle * RAG_COUNT + phase) * 0.52
           + np.sin(angle * (RAG_COUNT * 1.9 + 1.0) - phase * 0.6 + SEED) * 0.31
           + np.sin(angle * (RAG_COUNT * 3.3 + 2.0) + phase * 0.35 + SEED * 1.7) * 0.17)

    outward = p / np.maximum(span, 1e-5)[..., None]
    turn = CLOSE_ANGLE + close * CLOSE_SPIN
    closing = np.array([np.cos(turn), np.sin(turn)])
    lean = 0.55 + 0.45 * (outward @ closing)
    edge = np.maximum(radius * (1.0 + RAG * rag) * np.clip(1.0 - close * lean, 0, 1), 1e-5)

    bore = np.clip((edge - r) / aa, 0, 1)

    lump = (np.sin(p[..., 0] * GRAIN_SCALE + SEED * 3.1)
            * np.sin(p[..., 1] * GRAIN_SCALE * 1.27 - SEED * 2.2)
            + 0.55 * np.sin((p[..., 0] - p[..., 1]) * GRAIN_SCALE * 2.3 + SEED * 0.7))
    bite = (np.sin(p[..., 0] * GRAIN_SCALE * 0.85 + phase * BITE_DRIFT + SEED * 1.4)
            * np.sin(p[..., 1] * GRAIN_SCALE * 1.1 - phase * BITE_DRIFT * 0.76 + SEED * 2.6)
            + 0.55 * np.sin((p[..., 0] + p[..., 1]) * GRAIN_SCALE * 1.9
                            + phase * BITE_DRIFT * 0.52))
    intact = 0.5 + 0.32 * bite
    inside = bore * (intact > erode)

    fracture = np.zeros_like(r)
    for i in range(5):
        aim = SEED * 0.83 + i * 1.2566 + np.sin(SEED + i * 2.7) * 0.66
        delta = angle - aim + 0.14 * np.sin(r * 7.0 + SEED * 1.9 + i * 2.2)
        delta = np.arctan2(np.sin(delta), np.cos(delta))
        run = (CRACK_LENGTH * (0.68 + 0.32 * hash1(SEED * 1.7 + i * 5.3))
               * np.clip(crack * (1.35 - 0.6 * hash1(SEED * 2.3 + i * 3.9)), 0, 1))
        along = np.clip((run - r) / np.maximum(run - edge, 1e-3), 0, 1)
        width = (0.035 + 0.035 * hash1(SEED * 2.9 + i * 7.1)) * along
        fracture = np.maximum(fracture, np.clip(1.0 - np.abs(delta) / np.maximum(width, 1e-5), 0, 1))
    fracture = fracture * (1.0 - bore)
    crack_mask = (fracture > 0.18).astype(np.float64)

    alpha = np.maximum(inside, crack_mask)

    depth = np.clip((edge - r) / np.maximum(edge, 1e-5), 0, 1)
    far = np.clip(0.5 + 0.5 * outward[..., 1], 0, 1)

    lip_width = 0.028 + 0.062 * (1.0 - far)
    lip = np.clip((lip_width - depth) / np.maximum(lip_width, 1e-5), 0, 1)
    wall_width = 0.06 + 0.34 * far
    wall = np.clip((wall_width - depth) / np.maximum(wall_width, 1e-5), 0, 1)

    matter = np.broadcast_to(HOLE_FLOOR, p.shape[:-1] + (3,)).copy()
    matter = lerp(matter, HOLE_WALL, (wall * wall * far)[..., None])
    matter = lerp(matter, HOLE_LIP, (lip * lip * lip)[..., None])
    matter = matter * np.clip(1.0 + GRAIN * 0.7 * lump, 0, None)[..., None]

    unit = radius * 0.5
    main = pool_distance(p, np.array([-0.28, 0.50]) * unit, np.array([1.45, 1.0]),
                         0.50 * unit, 0.26, SEED * 2.1)
    spill = pool_distance(p, np.array([0.44, -0.26]) * unit, np.array([1.0, 1.3]),
                          0.27 * unit, 0.34, SEED * 3.7 + 2.0)

    main_wash = np.clip(1.0 - main, 0, 1)
    spill_wash = np.clip(1.0 - spill, 0, 1)
    wash = np.maximum(main_wash ** 2, spill_wash ** 2 * 0.55)
    main_core = np.clip((0.44 - main) / 0.30, 0, 1)
    spill_core = np.clip((0.30 - spill) / 0.30, 0, 1)
    core = np.maximum(main_core ** 2, spill_core ** 2 * 0.55)
    main_glint = np.clip((0.115 - main) / 0.05, 0, 1)
    spill_glint = np.clip((0.14 - spill) / 0.07, 0, 1)
    glint = np.maximum(main_glint ** 2, spill_glint ** 2 * 0.85)

    light = (VOID_WASH * wash[..., None] + VOID_CORE * core[..., None]
             + VOID_GLINT * glint[..., None]) * glow

    fissure = lerp(HOLE_LIP, HOLE_FLOOR, np.clip((fracture - 0.25) / 0.55, 0, 1)[..., None])
    fissure = fissure + VOID_WASH * (crack_glow * 0.8
                                     * np.clip((fracture - 0.58) / 0.42, 0, 1))[..., None]

    color = lerp(fissure, matter + light, inside[..., None])
    return color, alpha


SHARD_SINK_S = 0.10


def build_shards():
    rng = np.random.default_rng(11)
    hole_radius = HOLE_CELLS * CELL * 0.5
    start = rng.uniform(0, 2 * np.pi)
    out = []
    for i in range(7):
        angle = start + i * 2.39996 + rng.uniform(-0.5, 0.5)
        outward = np.array([np.cos(angle), 0.0, np.sin(angle)])
        width = (0.6 if i == 0 else rng.uniform(0.28, 0.36) if i < 3 else rng.uniform(0.11, 0.17)) * CELL
        lip = rng.uniform(0.44, 0.66) * hole_radius
        out.append(dict(
            frm=outward * lip + np.array([0, rng.uniform(0.06, 0.26), 0]),
            to=outward * (lip * rng.uniform(0.2, 0.5)) + np.array([0, width * 0.12, 0]),
            width=width,
            delay=0.0 if i == 0 else rng.uniform(0.02, 0.24),
            fall=rng.uniform(0.14, 0.26),
        ))
    return out


SHARDS = build_shards()


def shard_mask(gx, gy, t):
    hit = np.zeros(gx.shape, dtype=bool)
    for shard in SHARDS:
        age = t - shard["delay"]
        if age <= 0:
            continue
        sinking = np.clip((age - shard["fall"]) / SHARD_SINK_S, 0, 1)
        if sinking >= 1:
            continue
        fall = min(age / shard["fall"], 1.0)
        pos = lerp(shard["frm"], shard["to"], 1.0 - (1.0 - fall) ** 2)
        pos[1] = lerp(shard["frm"][1], shard["to"][1], fall * fall) - sinking * shard["width"] * 0.9
        cx = pos[0] * UNIT_PX
        cy = (HEIGHT_SQUASH * pos[1] + DEPTH_SQUASH * pos[2]) * UNIT_PX
        # the rag and the two straight fractures take roughly a third of the card away
        radius = 0.5 * shard["width"] * UNIT_PX * 0.82 * lerp(0.72, 1.0, min(age / 0.045, 1.0))
        cut = 1.0 - sinking
        hit |= (((gx - cx) ** 2 + (gy - cy) ** 2 <= radius ** 2)
                & (gy - cy <= radius * (2 * cut - 1)))
    return hit


def body_mask(gx, gy, t):
    """Where the corpse occludes: the part of it still above the deck, projected."""
    sink = np.clip(t / SINK_S, 0, 1) ** 1.35
    swallow = np.clip((t - SINK_S - SUBMERGED_S) / SWALLOW_S, 0, 1) ** 2
    if t >= SINK_S + SUBMERGED_S + SWALLOW_S:
        return np.zeros(gx.shape, dtype=bool), 0.0
    squat = lerp(1.0, CRUSH_HEIGHT, sink) * lerp(1.0, SWALLOW_HEIGHT, swallow)
    spread = lerp(1.0, CRUSH_SPREAD, sink) * lerp(1.0, 0.86, swallow)
    depth = SINK_DEPTH * sink + SWALLOW_DEPTH * swallow

    top = 1.7 * squat - depth
    if top <= 0.034:
        return np.zeros(gx.shape, dtype=bool), 0.0

    half_w = 0.45 * spread * UNIT_PX
    near = (0.292 * 0.034 - 0.45 * spread * DEPTH_SQUASH) * UNIT_PX
    farthest = (0.292 * top + 0.45 * spread * DEPTH_SQUASH) * UNIT_PX
    cx, cy = 0.0, 0.5 * (near + farthest)
    half_h = 0.5 * (farthest - near)
    inside = ((gx - cx) / half_w) ** 2 + ((gy - cy) / half_h) ** 2 <= 1.0
    return inside, top


def frame(t):
    quad_world = HOLE_CELLS * CELL * HOLE_PAD
    half = int(quad_world * UNIT_PX * 0.5) + 8
    xs = np.arange(-half, half + 1, dtype=np.float64)
    ys = np.arange(-half, half + 1, dtype=np.float64)
    gx, gy = np.meshgrid(xs, ys)

    # ground plane to quad uv: the depth axis is foreshortened by sin(73)
    px = gx / (quad_world * UNIT_PX)
    py = gy / (quad_world * UNIT_PX * DEPTH_SQUASH)
    p = np.stack([px, py], axis=-1)
    aa = 2.0 * 1.5 / (quad_world * UNIT_PX)

    radius, close, erode, phase, glow, crack, crack_glow = hole_state(t)
    color, alpha = render_hole(p, aa, radius, close, erode, phase, glow, crack, crack_glow)
    linear = BOARD * (1 - alpha[..., None]) + color * alpha[..., None]

    plates = shard_mask(gx, -gy, t)
    linear = np.where(plates[..., None], np.array([0.030, 0.040, 0.048]), linear)

    body, _ = body_mask(gx, -gy, t)
    linear = np.where(body[..., None], np.array([0.019, 0.022, 0.027]), linear)

    return grade(linear.reshape(-1, 3)).reshape(linear.shape), body


def measure():
    board = grade(BOARD[None, :])[0]
    board_l = 0.2126 * board[0] + 0.7152 * board[1] + 0.0722 * board[2]
    print(f"board in frame: rgb {tuple(int(v) for v in board)}  L {board_l:.0f}")
    print()
    header = ("  t     area(cells2)  Lmed  Lp90  sat_med  hue_med  fullclip  bright+sat  IoU(prev)")
    print(header)

    previous = None
    worst = 0.0
    for step in range(0, 32):
        t = step / 30.0
        if t > LIFE_S + 0.05:
            break
        rgb, _ = frame(t)
        lum = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]
        mask = lum < (board_l - 25)
        area = mask.sum() / (CELL_PX ** 2 * DEPTH_SQUASH)

        iou = float("nan")
        if previous is not None and (mask | previous).sum() > 0:
            iou = (mask & previous).sum() / (mask | previous).sum()
            if t > 0.05:
                worst = max(worst, iou)
        previous = mask

        if mask.sum() == 0:
            print(f" {t:.3f}  {area:11.2f}   --     --      --       --        --         --      {iou:.3f}")
            continue

        sel = rgb[mask]
        mx, mn = sel.max(-1), sel.min(-1)
        sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
        hue = np.zeros(len(sel))
        d = np.maximum(mx - mn, 1e-6)
        m = mx == sel[:, 0]
        hue[m] = (60 * ((sel[:, 1] - sel[:, 2]) / d))[m]
        m = mx == sel[:, 1]
        hue[m] = (60 * (2 + (sel[:, 2] - sel[:, 0]) / d))[m]
        m = mx == sel[:, 2]
        hue[m] = (60 * (4 + (sel[:, 0] - sel[:, 1]) / d))[m]
        hue %= 360
        full = int((rgb.min(-1) >= 254).sum())
        bright = int(((lum > 140) & (np.where(rgb.max(-1) > 1e-6,
                     (rgb.max(-1) - rgb.min(-1)) / np.maximum(rgb.max(-1), 1e-6), 0) > 0.45)).sum())
        print(f" {t:.3f}  {area:11.2f}  {np.median(lum[mask]):5.0f} "
              f"{np.percentile(lum[mask], 90):5.0f}   {np.median(sat):.3f}   "
              f"{np.median(hue[sat > 0.2]) if (sat > 0.2).any() else float('nan'):6.0f}   "
              f"{full:7d}    {bright:7d}     {iou:.3f}")

    print()
    print(f"worst consecutive-frame IoU after +0.05s: {worst:.3f}")


def sheet():
    frames = [0.0, 0.033, 0.067, 0.10, 0.133, 0.167, 0.20, 0.233,
              0.267, 0.30, 0.367, 0.433, 0.533, 0.667, 0.80, 0.933]
    tiles = [Image.fromarray(frame(t)[0].astype(np.uint8)) for t in frames]
    w, h = tiles[0].size
    out = Image.new("RGB", (4 * w, 4 * h))
    for i, im in enumerate(tiles):
        out.paste(im, ((i % 4) * w, (i // 4) * h))
    out.save("_b4_sim.png")
    print(f"wrote _b4_sim.png  tile {w}x{h}  frames {frames}")


def sweep():
    import itertools
    import sys

    base = dict(CLOSE_S=CLOSE_S, CLOSE_GAIN=CLOSE_GAIN, CLOSE_SHAPE=CLOSE_SHAPE,
                ERODE_SHAPE=ERODE_SHAPE, PHASE_RATE=PHASE_RATE)
    module = sys.modules[__name__]
    print(" close  gain  shape  erode  phase | worstIoU(close)  area@0.60  peakframe")
    for close_s, shape, erode_shape, phase in itertools.product(
        (0.60, 0.68), (0.72, 0.9), (0.62, 0.8), (11.0, 16.0)
    ):
        setattr(module, "CLOSE_S", close_s)
        setattr(module, "CLOSE_SHAPE", shape)
        setattr(module, "ERODE_SHAPE", erode_shape)
        setattr(module, "PHASE_RATE", phase)

        board = grade(BOARD[None, :])[0]
        board_l = 0.2126 * board[0] + 0.7152 * board[1] + 0.0722 * board[2]
        previous, worst, area_at, best, best_t = None, 0.0, 0.0, -1.0, 0.0
        for step in range(0, 30):
            t = step / 30.0
            rgb, _ = frame(t)
            lum = 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]
            mask = lum < (board_l - 25)
            area = mask.sum() / (CELL_PX ** 2 * DEPTH_SQUASH)
            mx, mn = rgb.max(-1), rgb.min(-1)
            sat = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
            score = (int((rgb.min(-1) >= 254).sum()) / 200.0
                     + area / 1.4
                     + int(((lum > 140) & (sat > 0.45)).sum()) / 3200.0)
            if score > best:
                best, best_t = score, t
            if previous is not None and (mask | previous).sum() and PEAK_S < t < LIFE_S - 0.1:
                worst = max(worst, (mask & previous).sum() / (mask | previous).sum())
            previous = mask
            if abs(t - 0.60) < 0.017:
                area_at = area
        print(f" {close_s:.2f}  {CLOSE_GAIN:.2f}  {shape:.2f}   {erode_shape:.2f}  "
              f"{phase:4.1f}  |     {worst:.3f}        {area_at:.2f}      f{int(round(best_t*30))+12}"
              f" ({best_t:+.3f}s)")

    for key, value in base.items():
        setattr(module, key, value)


if __name__ == "__main__":
    import sys

    if "sweep" in sys.argv:
        sweep()
    else:
        measure()
        sheet()
