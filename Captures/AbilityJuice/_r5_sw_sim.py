"""Round 5 simulator for BP_ShockwaveDust / BP_ShockwaveEdge / BP_ShockwaveCore.

Renders one shockwave frame at the SHOCKWAVE SHOT's own geometry (FOV 60, distance 13, square
1024 target => 68.24 px per world unit) and Lanczos-halves it to the 512x512 strip panel, so a
percentage printed here is a percentage of the panel the critic measures.

Everything this file prints is SIMULATED. `legacy=True` reproduces the shipped r4 shaders so the
simulator can be checked against the real captured frame before anything is tuned.
"""
import os
import sys

import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter, binary_erosion

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _b4_sw_pipe import s2l, grade, l2s, LSH  # noqa: E402

FRAME = 1024
PANEL = 512
PX_PER_WORLD = FRAME / (2.0 * 13.0 * np.tan(np.radians(30.0)))   # 68.24
FORESHORTEN = np.cos(np.radians(90.0 - 73.0))                    # 0.956
CELL = 2.7
CELL_PANEL_PX = CELL * PX_PER_WORLD / 2.0                        # 92.1 px on the panel
MAX_RADIUS = 4.3
QUAD_HALF = MAX_RADIUS * 1.42
DECK_SRGB = 0.714

BLOOM_THRESHOLD = 1.8
BLOOM_INTENSITY = 0.55
BLOOM_SCATTER = 0.55


def hash11(n):
    n = np.modf(np.asarray(n, dtype=np.float64) * 0.1031)[0]
    n = n * (n + 33.33)
    n = n * (n + n)
    return np.modf(n)[0]


def angle_noise(turns, cells, seed):
    scaled = turns * cells
    cell = np.floor(scaled)
    blend = scaled - cell
    blend = blend * blend * (3.0 - 2.0 * blend)
    a = hash11(np.mod(cell, cells) * 13.71 + seed)
    b = hash11(np.mod(cell + 1.0, cells) * 13.71 + seed)
    return (a + (b - a) * blend) * 2.0 - 1.0


def smoothstep(e0, e1, x):
    t = np.clip((x - e0) / np.maximum(np.asarray(e1) - np.asarray(e0), 1e-9), 0, 1)
    return t * t * (3 - 2 * t)


def lerp(a, b, t):
    return a + (b - a) * t


def front_opening(turns, seed, vents=4.0, vent_width=0.038):
    open_ = np.ones_like(turns)
    phase = hash11(seed * 0.61 + 4.9)
    for k in range(4):
        place = hash11(seed * 3.1 + k * 7.13 + 1.7)
        span = hash11(seed * 1.9 + k * 3.71 + 9.3)
        centre = (k + 0.10 + 0.80 * place) * 0.25 + phase
        half = vent_width * (0.45 + 0.85 * span)
        depth = np.clip(vents - k, 0, 1) * (0.55 if k == 3 else 1.0)
        gap = np.abs(np.mod(turns - centre + 0.5, 1.0) - 0.5)
        open_ = np.minimum(open_, lerp(1.0, smoothstep(half * 0.42, half, gap), depth))
    return open_


DIRS = np.array([(0.9848, 0.1736), (0.2079, -0.9781), (-0.6428, 0.7660),
                 (0.7660, 0.6428), (-0.3420, -0.9397)])
FREQ = np.array([1.00, 1.71, 2.63, 4.11, 6.37])
AMP = np.array([0.50, 0.31, 0.13, 0.05, 0.02])


def relief_field(pu, pv, scale, seed, slope_amp=None):
    """Height and slope from the same five octaves, but the slope may be weighted differently.

    The height spectrum decides how mottled the surface looks; the SLOPE spectrum decides how
    steep the shading gets per pixel. Weighting the slope toward the low octaves is what turns a
    terminator into a cluster of individually lit lumps without flattening the value range.
    """
    samp = AMP if slope_amp is None else np.asarray(slope_amp, dtype=np.float64)
    v = np.zeros_like(pu)
    gu = np.zeros_like(pu)
    gv = np.zeros_like(pu)
    fine = np.zeros_like(pu)
    for i in range(5):
        k = scale * FREQ[i]
        phase = (pu * DIRS[i, 0] + pv * DIRS[i, 1]) * k + seed * (1.7 + i * 0.83)
        s = np.sin(phase)
        c = np.cos(phase) * samp[i] * k
        v += s * AMP[i]
        gu += c * DIRS[i, 0]
        gv += c * DIRS[i, 1]
        if i >= 3:
            fine += s * AMP[i] * 2.6
    return v, gu, gv, fine


def board():
    """uv-space fields for a quad of half-extent QUAD_HALF centred in the frame."""
    yy, xx = np.mgrid[0:FRAME, 0:FRAME].astype(np.float64)
    c = FRAME / 2.0
    wx = (xx - c) / PX_PER_WORLD
    wy = (yy - c) / (PX_PER_WORLD * FORESHORTEN)
    return wx, wy


WX, WY = board()


def dust_layer(cfg, half, centre=(0.0, 0.0), collar=False):
    u = (WX - centre[0]) / (half * 2.0)
    v = (WY - centre[1]) / (half * 2.0)
    radius = np.hypot(u, v) * 2.0
    ang = np.arctan2(v, u)
    turns = ang * 0.15915494 + 0.5
    ln = np.maximum(np.hypot(u, v), 1e-5)
    fx, fy = u / ln, v / ln
    lx, ly = np.cos(np.radians(cfg['lean_deg'])), np.sin(np.radians(cfg['lean_deg']))
    heavy = 0.5 + 0.5 * (fx * lx + fy * ly)

    seed_shape = cfg['collar_seed'] if collar else cfg['shape_seed']
    outline = (angle_noise(turns, 7.0, seed_shape) * 0.62
               + angle_noise(turns, 14.0, seed_shape + 31.7) * 0.38)
    inner_line = angle_noise(turns, 10.0, seed_shape + 57.3)
    open_ = front_opening(turns, seed_shape)

    LEAN = 0.29
    inner_rag = 0.55 if collar else 0.42
    R = cfg['collar_radius'] if collar else cfg['radius']
    TH = cfg['collar_thickness'] if collar else cfg['thickness']
    lump_scale = cfg['lump_scale'] * (1.45 if collar else 1.0)
    wall_relief = cfg['wall_relief'] * (0.85 if collar else 1.0)
    erode = cfg['collar_erode'] if collar else cfg['erode']
    seed = cfg['collar_seed_grain'] if collar else cfg['seed']

    outer_r = R * (1 + 0.17 * outline) * lerp(1 - LEAN * 0.12, 1 + LEAN * 0.12, heavy) * lerp(0.62, 1.0, open_)
    band = TH * R * (1 + inner_rag * inner_line) * lerp(1 - LEAN, 1 + LEAN, heavy) * lerp(0.10, 1.0, open_)
    inner_r = np.maximum(outer_r - band, 0.008)

    aa = (1.0 / (half * PX_PER_WORLD)) * 1.2
    mask = np.clip((outer_r - radius) / aa, 0, 1) * np.clip((radius - inner_r) / aa, 0, 1)
    mask = mask * (open_ > 0.055)
    across = np.clip((radius - inner_r) / np.maximum(outer_r - inner_r, 1e-4), 0, 1)

    grain = (np.sin(ang * (5 * 3.3 + 5.0) + seed * 2.1 + across * 6.0) * 0.5
             + np.sin(ang * (5 * 7.1 + 2.0) - seed * 1.7 + across * 11.0) * 0.3
             + np.sin(ang * (5 * 12.7 + 3.0) + seed * 0.9 - across * 17.0) * 0.2)
    intact = 0.5 + 0.5 * grain
    mask = mask * np.clip((intact - erode) / 0.03, 0, 1)

    l0, dlu, dlv, fine = relief_field(u, v, lump_scale, seed, cfg.get('slope_amp'))
    wall = np.sin(across * np.pi)
    el = np.radians(cfg['key_elev'])
    if cfg['legacy']:
        dwall = np.pi * np.cos(across * np.pi)
        band_uv = np.maximum(outer_r - inner_r, 1e-4) * 0.5
        radial = dwall / band_uv * wall_relief
        sx = fx * radial + dlu * cfg['lump_relief']
        sy = fy * radial + dlv * cfg['lump_relief']
        inv = 1.0 / np.sqrt(sx * sx + sy * sy + 1.0)
        nx, ny, nz = -sx * inv, -sy * inv, inv
        ndl = nx * (-fx * np.cos(el)) + ny * (-fy * np.cos(el)) + nz * np.sin(el)
        keyterm = cfg['key'] * np.clip(ndl, 0, 1) ** cfg['key_gamma']
    else:
        sx, sy = dlu * cfg['lump_relief'], dlv * cfg['lump_relief']
        inv = 1.0 / np.sqrt(sx * sx + sy * sy + 1.0)
        nx, ny, nz = -sx * inv, -sy * inv, inv
        ndl = nx * (-fx * np.cos(el)) + ny * (-fy * np.cos(el)) + nz * np.sin(el)
        lump_key = np.clip(0.5 + 0.5 * ndl, 0, 1) ** cfg['key_gamma']
        form = 1.0 - smoothstep(cfg['form_in'], cfg['form_out'], across)
        sh = cfg['lump_share']
        keyterm = cfg['key'] * form * (1.0 - sh + (1.0 + 0.6 * sh - (1.0 - sh)) * lump_key)
    sky = np.clip((nx * 0.55 + ny * 0.84) * 0.62 + nz * 0.79, 0, 1)
    shade = np.clip(cfg['ambient'] + keyterm + cfg['sky'] * sky + cfg['micro'] * l0, 0, 1)
    key_share = np.clip(keyterm / np.maximum(shade, 1e-4) * cfg['tint_reach'], 0, 1)

    dark = s2l(np.array(cfg['dust_dark']))
    lit_c = s2l(np.array(cfg['dust_lit']))
    keylit = s2l(np.array(cfg['dust_keylit']))
    lit_here = lit_c[None, None, :] * (1 - key_share[..., None]) + keylit[None, None, :] * key_share[..., None]
    colour = dark[None, None, :] + (lit_here - dark[None, None, :]) * shade[..., None]
    return colour, np.clip(mask, 0, 1) * cfg['opacity'], dict(
        outer_r=outer_r, radius=radius, turns=turns, fx=fx, fy=fy, heavy=heavy,
        open_=open_, l0=l0, dlu=dlu, dlv=dlv, fine=fine, wall=wall)


def clod_layer(cfg):
    """The mound left in the crater: what currently makes the middle of the blast L96."""
    half = cfg['clod_half']
    u = WX / (half * 2.0)
    v = WY / (half * 2.0)
    radius = np.hypot(u, v) * 2.0
    ang = np.arctan2(v, u)
    ln = np.maximum(np.hypot(u, v), 1e-5)
    fx, fy = u / ln, v / ln
    seed = cfg['clod_seed']

    rag = (np.sin(ang * 4.0 + seed) * 0.50 + np.sin(ang * (4 * 1.7 + 1.0) - seed * 1.4) * 0.32
           + np.sin(ang * (4 * 2.7 + 2.0) + seed * 0.8) * 0.18)
    edge = (1.0 / 1.34) * (1.0 + 0.26 * rag)
    aa = (1.0 / (half * PX_PER_WORLD)) * 1.2
    mask = np.clip((edge - radius) / aa, 0, 1)

    crumble = (np.sin(u * 16.0 + seed * 3.1) * np.sin(v * 16.0 * 1.3 - seed * 2.2)
               + 0.6 * np.sin((u + v) * 16.0 * 2.1 + seed))
    intact = 0.5 + 0.35 * crumble + 0.3 * (fx * 1.0 + fy * 0.0)
    mask = mask * np.clip((intact - cfg['clod_erode']) / 0.03, 0, 1)

    l0, dlu, dlv, _ = relief_field(u, v, cfg['clod_lump_scale'], seed, cfg.get('slope_amp'))
    dome = np.clip(1.0 - radius / np.maximum(edge, 1e-4), 0, 1)
    el = np.radians(cfg['key_elev'])
    tb = np.array([-1.0, 0.0])
    if cfg['legacy']:
        sx = -fx * ((1.0 - dome) * 2.2 / np.maximum(edge, 1e-4)) + dlu * cfg['lump_relief']
        sy = -fy * ((1.0 - dome) * 2.2 / np.maximum(edge, 1e-4)) + dlv * cfg['lump_relief']
        inv = 1.0 / np.sqrt(sx * sx + sy * sy + 1.0)
        nx, ny, nz = -sx * inv, -sy * inv, inv
        ndl = (nx * tb[0] + ny * tb[1]) * np.cos(el) + nz * np.sin(el)
        keyterm = cfg['key'] * np.clip(ndl, 0, 1) ** cfg['key_gamma']
    else:
        sx, sy = dlu * cfg['lump_relief'], dlv * cfg['lump_relief']
        inv = 1.0 / np.sqrt(sx * sx + sy * sy + 1.0)
        nx, ny, nz = -sx * inv, -sy * inv, inv
        ndl = (nx * tb[0] + ny * tb[1]) * np.cos(el) + nz * np.sin(el)
        lump_key = np.clip(0.5 + 0.5 * ndl, 0, 1) ** cfg['key_gamma']
        toward = 1.0 - (0.5 + 0.5 * (fx * tb[0] + fy * tb[1]))
        form = (1.0 - smoothstep(cfg['form_in'], cfg['form_out'], toward)) * (0.30 + 0.70 * dome ** 0.5)
        sh = cfg['lump_share']
        keyterm = cfg['key'] * form * (1.0 - sh + (1.0 + 0.6 * sh - (1.0 - sh)) * lump_key)
    sky = np.clip((nx * 0.55 + ny * 0.84) * 0.62 + nz * 0.79, 0, 1)
    shade = np.clip(cfg['ambient'] + keyterm + cfg['sky'] * sky + cfg['micro'] * l0, 0, 1)
    key_share = np.clip(keyterm / np.maximum(shade, 1e-4) * cfg['tint_reach'], 0, 1)

    dark = s2l(np.array(cfg['clod_dark']))
    lit_c = s2l(np.array(cfg['clod_lit']))
    keylit = s2l(np.array(cfg['clod_keylit']))
    lit_here = lit_c[None, None, :] * (1 - key_share[..., None]) + keylit[None, None, :] * key_share[..., None]
    colour = dark[None, None, :] + (lit_here - dark[None, None, :]) * shade[..., None]
    return colour, np.clip(mask, 0, 1) * cfg['clod_opacity']


def crest_layer(cfg, geo, half):
    radius, turns, heavy = geo['radius'], geo['turns'], geo['heavy']
    fx, fy, open_, outer_r = geo['fx'], geo['fy'], geo['open_'], geo['outer_r']
    l0, dlu, dlv, fine = geo['l0'], geo['dlu'], geo['dlv'], geo['fine']

    on_material = smoothstep(0.15, 0.60, open_)
    arc_phase = hash11(cfg['arc_seed'] * 0.37 + 5.1)
    W = cfg['crest_width']
    from_edge = radius - outer_r
    glow = np.array(cfg['glow_lin'])
    ember = np.array(cfg['ember_lin'])
    core = np.array(cfg['core_lin'])

    csx, csy = dlu * cfg['lump_relief'], dlv * cfg['lump_relief']
    cinv = 1.0 / np.sqrt(csx * csx + csy * csy + 1.0)
    crest_face = np.clip((-csx * cinv) * fx + (-csy * cinv) * fy, 0, 1)

    acc = np.zeros((FRAME, FRAME, 3))
    for k in range(8):
        place = hash11(cfg['arc_seed'] * 2.7 + k * 5.13 + 0.7)
        span_roll = hash11(cfg['arc_seed'] * 4.1 + k * 9.71 + 3.3)
        power = hash11(cfg['arc_seed'] * 1.3 + k * 2.57 + 7.9)
        push = hash11(cfg['arc_seed'] * 5.9 + k * 6.41 + 1.1)
        centre = (k + 0.08 + 0.84 * place) * 0.125 + arc_phase
        half_span = cfg['arc_span'] * (0.26 + 0.74 * span_roll * span_roll)
        gap = np.abs(np.mod(turns - centre + 0.5, 1.0) - 0.5)
        along = (np.sqrt(np.clip(1.0 - gap / max(half_span, 1e-4), 0, 1)) * on_material
                 * np.clip(cfg['arc_count'] - k, 0, 1))

        d = from_edge - W * (cfg['rim_push'] + 0.30 * (push - 0.5))
        depth = (cfg['slab_in'] * W * (0.70 + 0.60 * span_roll)
                 * (0.55 + 0.80 * np.clip(0.5 + 0.5 * l0, 0, 1))
                 * lerp(1.0 - cfg['streak'], 1.0 + cfg['streak'] * 0.5,
                        np.clip(0.5 + 0.62 * angle_noise(turns, cfg['streak_cells'],
                                                         cfg['arc_seed'] + 12.7), 0, 1)))
        shape = (along * (0.80 + 0.34 * power) * lerp(0.86, 1.10, heavy)
                 * lerp(1.0 - cfg['relief_bite'], 1.0, crest_face ** cfg['relief_gamma']))
        live = smoothstep(cfg['slab_cut'] - cfg['slab_soft'], cfg['slab_cut'] + cfg['slab_soft'], shape)
        prof = ((1.0 - smoothstep(cfg['slab_hold'] * depth, depth, -d))
                * (1.0 - smoothstep(0.0, cfg['slab_out'] * W, d)))
        body = live * prof * lerp(1.0 - cfg['slab_swing'], 1.0 + cfg['slab_swing'] * 0.4,
                                  np.clip(0.5 + 0.5 * fine, 0, 1))
        tail = cfg['tail_amp'] * np.clip(1.0 + d / (cfg['tail_in'] * W), 0, 1) ** cfg['tail_pow']
        outward = cfg['out_amp'] * np.clip(1.0 - d / (cfg['crest_out'] * W), 0, 1) ** cfg['out_pow']
        halo = np.where(d > 0, outward, tail) * along * lerp(0.86, 1.10, heavy)
        env = np.maximum(body, halo)
        if k == 0:
            gnd = (cfg['ground_amp']
                   * np.clip(1.0 - from_edge / (cfg['ground_reach'] * W), 0, 1) ** cfg['ground_pow']
                   * np.where(from_edge > 0, 1.0, 0.0) * lerp(0.55, 1.0, on_material)
                   * lerp(0.82, 1.12, heavy)
                   * lerp(0.42, 1.0, np.clip(0.5 + 0.6 * angle_noise(turns, 9.0, cfg['arc_seed'] + 3.3), 0, 1))
                   * np.clip((1.0 - radius) / 0.12, 0, 1))
            env = np.maximum(env, gnd)
        emberw = np.clip(1.0 - np.abs(d + W * 0.02) / (W * cfg['ember_share']), 0, 1) ** 1.6
        glintw = np.clip(1.0 - np.abs(d + W * 0.04) / (W * cfg['core_share']), 0, 1) ** 3.0
        tone = glow[None, None, :] * (1 - emberw)[..., None] + ember[None, None, :] * emberw[..., None]
        arc = (tone + core[None, None, :] * (glintw * live * cfg['glint_amount'])[..., None]) * env[..., None]
        acc = np.maximum(acc, arc)
    return acc * cfg['intensity']


def core_layer(cfg):
    """BP_ShockwaveCore: the white-clipping heart in the hole, sized in cells."""
    R = cfg['core_radius']
    if R <= 0.0 or cfg['core_opacity'] <= 0.0:
        z = np.zeros((FRAME, FRAME, 3))
        return z, np.zeros((FRAME, FRAME)), z

    u, v = WX / R, WY / R
    radius = np.hypot(u, v)
    ang = np.arctan2(v, u)
    turns = ang * 0.15915494 + 0.5

    rim = 1.0 + cfg['core_wobble'] * (angle_noise(turns, 5.0, cfg['core_seed']) * 0.66
                                      + angle_noise(turns, 11.0, cfg['core_seed'] + 21.3) * 0.34)
    bite_h, _, _, _ = relief_field(u * 0.25, v * 0.25, cfg['core_bite_scale'], cfg['core_seed'])
    rim = rim * (1.0 - cfg['core_bite'] * np.clip(0.5 + 0.75 * bite_h, 0, 1))
    edge = np.maximum(rim, 0.35)
    t = np.clip(radius / edge, 0, 1)

    white = np.array(cfg['core_white'])
    hot = np.array(cfg['core_hot'])
    hue = np.array(cfg['core_hue'])
    a = smoothstep(cfg['core_plateau'], cfg['core_mid'], t)
    b = smoothstep(cfg['core_mid'], 1.0, t)
    colour = white[None, None, :] * (1 - a)[..., None] + hot[None, None, :] * a[..., None]
    colour = colour * (1 - b)[..., None] + hue[None, None, :] * b[..., None]

    aa = 1.4 / (R * PX_PER_WORLD)
    mask = np.clip((edge - radius) / aa, 0, 1) * cfg['core_opacity']

    # The skirt: the ability's hue thrown onto whatever is round the core, so the white sits
    # inside colour instead of on bare deck. Light, so it is allowed to be soft.
    skirt_t = np.clip((radius - edge) / max(cfg['skirt_reach'], 1e-4), 0, 1)
    skirt = (1.0 - skirt_t) ** cfg['skirt_pow'] * np.where(radius > edge, 1.0, 0.0)
    skirt = skirt * lerp(0.55, 1.0, np.clip(0.5 + 0.6 * angle_noise(turns, 7.0, cfg['core_seed'] + 8.1), 0, 1))
    add = (np.array(cfg['skirt_colour']) * cfg['skirt_intensity'])[None, None, :] * (skirt * cfg['core_opacity'])[..., None]
    return colour, mask, add


def bloom(linear):
    """URP-shaped bloom: soft-knee prefilter, a mip pyramid, scatter-weighted upsample."""
    lum = 0.2126 * linear[..., 0] + 0.7152 * linear[..., 1] + 0.0722 * linear[..., 2]
    knee = BLOOM_THRESHOLD * 0.5
    soft = np.clip(lum - BLOOM_THRESHOLD + knee, 0, 2 * knee)
    soft = soft * soft / (4 * knee + 1e-6)
    weight = np.maximum(np.maximum(soft, lum - BLOOM_THRESHOLD), 0) / np.maximum(lum, 1e-4)
    seed = linear * weight[..., None]

    total = np.zeros_like(linear)
    w = 1.0
    norm = 0.0
    for level in range(1, 6):
        total += gaussian_filter(seed, sigma=(2.0 ** level, 2.0 ** level, 0)) * w
        norm += w
        w *= BLOOM_SCATTER
    return total / max(norm, 1e-6)


def compose(cfg):
    deck = np.full((FRAME, FRAME, 3), s2l(DECK_SRGB))
    out = deck.copy()

    if cfg['clod_opacity'] > 0:
        c, a = clod_layer(cfg)
        out = c * a[..., None] + out * (1 - a[..., None])
    if cfg['collar_opacity'] > 0:
        cc = dict(cfg, opacity=cfg['collar_opacity'])
        c, a, _ = dust_layer(cc, cfg['collar_half'], centre=cfg['collar_centre'], collar=True)
        out = c * a[..., None] + out * (1 - a[..., None])

    c, a, geo = dust_layer(cfg, QUAD_HALF)
    out = c * a[..., None] + out * (1 - a[..., None])
    dust_alpha = a

    out = out + crest_layer(cfg, geo, QUAD_HALF)

    cc, ca, cadd = core_layer(cfg)
    out = out + cadd
    out = cc * ca[..., None] + out * (1 - ca[..., None])

    if cfg.get('bloom', True):
        out = out + bloom(out) * BLOOM_INTENSITY

    px = np.clip(l2s(grade(out)) * 255.0, 0, 255)
    plate = np.clip(l2s(grade(deck)) * 255.0, 0, 255)
    return px, plate, dust_alpha, ca


def to_panel(px):
    return np.asarray(
        Image.fromarray(np.clip(px, 0, 255).astype(np.uint8)).resize((PANEL, PANEL), Image.LANCZOS),
        dtype=np.float64,
    )


def report(tag, cfg, quiet=False):
    px, plate, dust_alpha, core_alpha = compose(cfg)
    p = to_panel(px)
    pl = to_panel(plate)
    L, S, H = LSH(p)
    Lp, _, _ = LSH(pl)

    hot = L > 240
    clip3 = (p >= 250).all(-1)
    changed = np.abs(p - plate[0, 0]).max(-1) > 20
    mass = changed & (L < Lp - 25)
    interior = binary_erosion(mass, np.ones((9, 9)))
    gy, gx = np.gradient(L)
    grad = np.hypot(gy, gx)

    bs = changed & (L > 200) & (S > 0.45)
    out = dict(
        hot=100.0 * hot.mean(), clip3=100.0 * clip3.mean(), lmax=float(L.max()),
        mass=int(mass.sum()), interior=int(interior.sum()),
        bs=100.0 * bs.mean(),
    )
    if interior.sum() > 200:
        g = grad[interior]
        li = L[interior]
        out.update(g99=float(np.percentile(g, 99)), g90=float(np.percentile(g, 90)),
                   gmean=float(g.mean()), istd=float(li.std()),
                   ip5=float(np.percentile(li, 5)), ip95=float(np.percentile(li, 95)))
        out['ispread'] = out['ip95'] - out['ip5']
    if bs.sum() > 50:
        out.update(bs_hue=float(np.median(H[bs])), bs_sat=float(np.median(S[bs])))
    yy, xx = np.mgrid[0:PANEL, 0:PANEL]
    mid = (yy - PANEL / 2) ** 2 + (xx - PANEL / 2) ** 2 < 30 ** 2
    out['centreL'] = float(L[mid].mean())

    if not quiet:
        print('--- %s   [SIMULATED]' % tag)
        print('  >L240 %6.3f%%   >=250 all 3 %6.3f%%   Lmax %5.1f   centre L %5.1f'
              % (out['hot'], out['clip3'], out['lmax'], out['centreL']))
        print('  dark mass %6d px (%.2f cells2)  interior %6d px' %
              (out['mass'], out['mass'] / CELL_PANEL_PX ** 2, out['interior']))
        if 'g99' in out:
            print('  interior |grad L|: mean %.2f  p90 %.2f  p99 %.2f' % (out['gmean'], out['g90'], out['g99']))
            print('  interior L: std %.1f  p5-p95 %.0f-%.0f  spread %.0f'
                  % (out['istd'], out['ip5'], out['ip95'], out['ispread']))
        print('  bright&saturated %.3f%%  hue %3.0f  sat %.2f'
              % (out['bs'], out.get('bs_hue', 0), out.get('bs_sat', 0)))
    return out, p
