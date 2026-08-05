"""Numerical port of BP_ShockwaveDust + BP_ShockwaveEdge at the money frame (f17, +0.167s).

Renders the front and the crest on a synthetic deck at the measured scale (72 px/world, 0.956
vertical foreshortening), pushes the result through the calibrated pipeline model, and reports the
critic's exact statistics: eroded-core luminance std / p5-p95, bright-and-saturated share, and the
floor spill within a 41px window.  `legacy=True` reproduces the shipped r3 shaders so the simulator
can be checked against the real captured frame before anything is tuned.
"""
import numpy as np
from scipy.ndimage import minimum_filter, maximum_filter
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _b4_sw_pipe import s2l, screen, LSH

N = 900
PX_PER_WORLD = 72.0
FORESHORTEN = 0.956
MAX_RADIUS = 4.3
QUAD_HALF = MAX_RADIUS * 1.42
DECK_SRGB = 0.714
CELL_PX = 194.0


def hash11(n):
    n = np.modf(n * 0.1031)[0]
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
        gate = smoothstep(half * 0.42, half, gap)
        open_ = np.minimum(open_, lerp(1.0, gate, depth))
    return open_


DIRS = np.array([(0.9848, 0.1736), (0.2079, -0.9781), (-0.6428, 0.7660),
                 (0.7660, 0.6428), (-0.3420, -0.9397)])
FREQ = np.array([1.00, 1.71, 2.63, 4.11, 6.37])
AMP = np.array([0.50, 0.31, 0.13, 0.05, 0.02])


def relief_field(pu, pv, scale, seed):
    """Isotropic lumpy relief with an analytic gradient. Five incommensurate directions, so it
    never resolves into the corduroy a product of two axis-aligned sines gives."""
    v = np.zeros_like(pu)
    gu = np.zeros_like(pu)
    gv = np.zeros_like(pu)
    fine = np.zeros_like(pu)
    for i in range(5):
        k = scale * FREQ[i]
        phase = (pu * DIRS[i, 0] + pv * DIRS[i, 1]) * k + seed * (1.7 + i * 0.83)
        s = np.sin(phase)
        c = np.cos(phase) * AMP[i] * k
        v += s * AMP[i]
        gu += c * DIRS[i, 0]
        gv += c * DIRS[i, 1]
        if i >= 3:
            fine += s * AMP[i] * 2.6
    return v, gu, gv, fine


def geometry(seed_shape, lean_deg):
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float64)
    c = N / 2.0
    wx = (xx - c) / PX_PER_WORLD
    wy = (yy - c) / (PX_PER_WORLD * FORESHORTEN)
    u = wx / (QUAD_HALF * 2.0)
    v = wy / (QUAD_HALF * 2.0)
    radius = np.hypot(u, v) * 2.0
    ang = np.arctan2(v, u)
    turns = ang * 0.15915494 + 0.5
    ln = np.hypot(u, v)
    fx = u / np.maximum(ln, 1e-5)
    fy = v / np.maximum(ln, 1e-5)
    lx, ly = np.cos(np.radians(lean_deg)), np.sin(np.radians(lean_deg))
    heavy = 0.5 + 0.5 * (fx * lx + fy * ly)
    return dict(u=u, v=v, radius=radius, ang=ang, turns=turns, fx=fx, fy=fy, heavy=heavy,
                seed_shape=seed_shape)


def silhouette(g):
    turns, heavy = g['turns'], g['heavy']
    s = g['seed_shape']
    outline = (angle_noise(turns, 7.0, s) * 0.62 + angle_noise(turns, 14.0, s + 31.7) * 0.38)
    inner_line = angle_noise(turns, 10.0, s + 57.3)
    open_ = front_opening(turns, s)
    return outline, inner_line, open_


def render(cfg):
    g = geometry(cfg['shape_seed'], cfg['lean_deg'])
    u, v, radius, ang, turns = g['u'], g['v'], g['radius'], g['ang'], g['turns']
    fx, fy, heavy = g['fx'], g['fy'], g['heavy']
    outline, inner_line, open_ = silhouette(g)
    LEAN = 0.29
    R, TH = cfg['radius'], cfg['thickness']

    outer_r = R * (1 + 0.17 * outline) * lerp(1 - LEAN * 0.12, 1 + LEAN * 0.12, heavy) * lerp(0.62, 1.0, open_)
    band = TH * R * (1 + 0.42 * inner_line) * lerp(1 - LEAN, 1 + LEAN, heavy) * lerp(0.10, 1.0, open_)
    inner_r = np.maximum(outer_r - band, 0.008)

    aa = (1.0 / (QUAD_HALF * PX_PER_WORLD)) * 1.2
    mask = np.clip((outer_r - radius) / aa, 0, 1) * np.clip((radius - inner_r) / aa, 0, 1)
    mask = mask * (open_ > 0.055)
    across = np.clip((radius - inner_r) / np.maximum(outer_r - inner_r, 1e-4), 0, 1)

    grain = (np.sin(ang * (5 * 3.3 + 5.0) + cfg['seed'] * 2.1 + across * 6.0) * 0.5 +
             np.sin(ang * (5 * 7.1 + 2.0) - cfg['seed'] * 1.7 + across * 11.0) * 0.3 +
             np.sin(ang * (5 * 12.7 + 3.0) + cfg['seed'] * 0.9 - across * 17.0) * 0.2)
    intact = 0.5 + 0.5 * grain
    mask = mask * np.clip((intact - cfg['erode']) / 0.03, 0, 1)

    l0, dlu, dlv, fine = relief_field(u, v, cfg['lump_scale'], cfg['seed'])
    wall = np.sin(np.clip(across, 0, 1) * np.pi)
    dwall = np.pi * np.cos(np.clip(across, 0, 1) * np.pi)
    band_uv = np.maximum(outer_r - inner_r, 1e-4) * 0.5
    radial_slope = dwall / band_uv * cfg['wall_relief']
    sx = fx * radial_slope + dlu * cfg['lump_relief']
    sy = fy * radial_slope + dlv * cfg['lump_relief']
    inv = 1.0 / np.sqrt(sx * sx + sy * sy + 1.0)
    nx, ny, nz = -sx * inv, -sy * inv, inv
    # As BP_ShockwaveEdge computes it: lump slope only, since the crest quad has no band to read.
    csx, csy = dlu * cfg['lump_relief'], dlv * cfg['lump_relief']
    cinv = 1.0 / np.sqrt(csx * csx + csy * csy + 1.0)
    crest_face = np.clip((-csx * cinv) * fx + (-csy * cinv) * fy, 0, 1)

    if cfg['legacy']:
        lit = np.clip(1.0 - across, 0, 1) ** 1.6
        side = (fx * 0.55 + fy * 0.84) * 0.5
        shade = np.clip(0.10 + 0.82 * lit + 0.26 * side + 0.34 * grain * 0.5, 0, 1)
    else:
        el = np.radians(cfg['key_elev'])
        kx, ky, kz = -fx * np.cos(el), -fy * np.cos(el), np.sin(el)
        lam = np.clip(nx * kx + ny * ky + nz * kz, 0, 1) ** cfg['key_gamma']
        sky = np.clip((nx * 0.55 + ny * 0.84) * 0.62 + nz * 0.79, 0, 1)
        keyterm = cfg['key'] * lam
        shade = np.clip(cfg['ambient'] + keyterm + cfg['sky'] * sky +
                        cfg['micro'] * l0 + cfg['wall_ao'] * wall, 0, 1)
        key_share = keyterm / np.maximum(shade, 1e-4)

    dark = s2l(np.array(cfg['dust_dark']))
    lit_c = s2l(np.array(cfg['dust_lit']))
    if cfg['legacy']:
        lit_here = np.broadcast_to(lit_c, shade.shape + (3,))
    else:
        # the blast is coloured, so the face it lights carries that colour; the shadow keeps the
        # dust's own warm albedo. Warm dark against cool light is the hue split the board lacks.
        keylit = s2l(np.array(cfg['dust_keylit']))
        w = np.clip(key_share * cfg['tint_reach'], 0, 1)[..., None]
        lit_here = lit_c[None, None, :] * (1 - w) + keylit[None, None, :] * w
    dust_lin = dark[None, None, :] + (lit_here - dark[None, None, :]) * shade[..., None]

    # ---- crest
    on_material = smoothstep(0.15, 0.60, open_)
    arc_phase = hash11(cfg['arc_seed'] * 0.37 + 5.1)
    W = cfg['crest_width']
    from_edge = radius - outer_r
    glow = np.array(cfg['glow_lin'])
    ember = np.array(cfg['ember_lin'])
    core = np.array(cfg['core_lin'])
    acc = np.zeros((N, N, 3))

    for k in range(8):
        place = hash11(cfg['arc_seed'] * 2.7 + k * 5.13 + 0.7)
        span_roll = hash11(cfg['arc_seed'] * 4.1 + k * 9.71 + 3.3)
        power = hash11(cfg['arc_seed'] * 1.3 + k * 2.57 + 7.9)
        push = hash11(cfg['arc_seed'] * 5.9 + k * 6.41 + 1.1)
        centre = (k + 0.08 + 0.84 * place) * 0.125 + arc_phase
        half_span = cfg['arc_span'] * (0.26 + 0.74 * span_roll * span_roll)
        gap = np.abs(np.mod(turns - centre + 0.5, 1.0) - 0.5)
        along = np.sqrt(np.clip(1.0 - gap / max(half_span, 1e-4), 0, 1)) * on_material * np.clip(cfg['arc_count'] - k, 0, 1)

        if cfg['legacy']:
            arc_centre = outer_r + W * (cfg['rim_push'] + 0.42 * (push - 0.5))
            acr = (radius - (arc_centre - W * 0.5)) / max(W, 1e-4)
            reach = np.where(acr > 0.5, 0.4, 0.55)
            env = np.clip(1.0 - np.abs(acr - 0.5) / reach, 0, 1) ** cfg['falloff'] * along
            tone = glow[None, None, :] + (ember - glow)[None, None, :] * smoothstep(0.44, 0.58, acr)[..., None]
            glint = np.clip(1.0 - np.abs(acr - 0.51) / cfg['core_share'], 0, 1) ** 2.6
            arc = (tone + core[None, None, :] * (glint * 1.35)[..., None]) * (env * (0.68 + 0.40 * power) * lerp(0.78, 1.14, heavy))[..., None]
        else:
            offs = W * (cfg['rim_push'] + 0.30 * (push - 0.5))
            d = from_edge - offs
            # How deep the burning face runs, not how bright it is: dimming the slab drops the
            # whole band under L200 and gives the colour back. Its inner edge wanders with the
            # dust's own lumps, so the hot face is torn rather than a constant-width insert.
            depth = (cfg['slab_in'] * W * (0.70 + 0.60 * span_roll) *
                     (0.55 + 0.80 * np.clip(0.5 + 0.5 * l0, 0, 1)) *
                     lerp(1.0 - cfg['streak'], 1.0 + cfg['streak'] * 0.5,
                          np.clip(0.5 + 0.62 * angle_noise(turns, cfg['streak_cells'],
                                                           cfg['arc_seed'] + 12.7), 0, 1)))
            shape = along * (0.80 + 0.34 * power) * lerp(0.86, 1.10, heavy) * \
                lerp(1.0 - cfg['relief_bite'], 1.0, crest_face ** cfg['relief_gamma'])
            live = smoothstep(cfg['slab_cut'] - cfg['slab_soft'], cfg['slab_cut'] + cfg['slab_soft'], shape)
            prof = ((1.0 - smoothstep(cfg['slab_hold'] * depth, depth, -d)) *
                    (1.0 - smoothstep(0.0, cfg['slab_out'] * W, d)))
            # variation held inside the window where the colour still reads as bright and saturated
            body = live * prof * lerp(1.0 - cfg['slab_swing'], 1.0 + cfg['slab_swing'] * 0.4,
                                      np.clip(0.5 + 0.5 * fine, 0, 1))
            tail = cfg['tail_amp'] * np.clip(1.0 + d / (cfg['tail_in'] * W), 0, 1) ** cfg['tail_pow']
            outward = cfg['out_amp'] * np.clip(1.0 - d / (cfg['crest_out'] * W), 0, 1) ** cfg['out_pow']
            halo = np.where(d > 0, outward, np.maximum(tail, outward * 0.0)) * along * lerp(0.86, 1.10, heavy)
            env = np.maximum(body, halo)
            if k == 0:
                gnd = (cfg['ground_amp'] *
                       np.clip(1.0 - from_edge / (cfg['ground_reach'] * W), 0, 1) ** cfg['ground_pow'] *
                       np.where(from_edge > 0, 1.0, 0.0) * lerp(0.55, 1.0, on_material) *
                       lerp(0.82, 1.12, heavy) *
                       lerp(0.42, 1.0, np.clip(0.5 + 0.6 * angle_noise(turns, 9.0, cfg['arc_seed'] + 3.3), 0, 1)) *
                       np.clip((1.0 - radius) / 0.12, 0, 1))
                env = np.maximum(env, gnd)
            emberw = np.clip(1.0 - np.abs(d + W * 0.02) / (W * cfg['ember_share']), 0, 1) ** 1.6
            glintw = np.clip(1.0 - np.abs(d + W * 0.04) / (W * cfg['core_share']), 0, 1) ** 3.0
            tone = glow[None, None, :] * (1 - emberw)[..., None] + ember[None, None, :] * emberw[..., None]
            arc = (tone + core[None, None, :] * (glintw * live)[..., None]) * env[..., None]

        acc = np.maximum(acc, arc)

    crest = acc * cfg['intensity']

    deck = np.full((N, N, 3), s2l(DECK_SRGB))
    a = np.clip(mask, 0, 1)[..., None] * cfg['opacity']
    out = dust_lin * a + deck * (1 - a) + crest
    return screen(out), screen(deck)


def metrics(px, plate, tag, quiet=False):
    L, S, H = LSH(px)
    Lp, _, _ = LSH(plate)
    changed = np.abs(px - plate).max(-1) > 20
    m = changed & (L < Lp - 25)
    core = minimum_filter(m.astype(np.uint8), size=17) > 0
    fx = np.abs(px - plate).max(-1) > 18
    bs = fx & (L > 200) & (S > 0.45)
    hot = fx & (L > 210)
    hd = maximum_filter(hot.astype(np.uint8), size=41) > 0
    ring = hd & (~m) & (~hot)
    out = dict(mass=int(m.sum()), core=int(core.sum()), bs_px=int(bs.sum()),
               bs_pct=100.0 * bs.sum() / N ** 2, spill=0.0, std=0.0, spread=0.0,
               p5=0.0, p95=0.0, hue=0.0, sat=0.0)
    if core.sum() > 100:
        vc = L[core]
        out.update(std=float(vc.std()), p5=float(np.percentile(vc, 5)), p95=float(np.percentile(vc, 95)),
                   spread=float(np.percentile(vc, 95) - np.percentile(vc, 5)), mean=float(vc.mean()))
    if bs.sum() > 50:
        out.update(hue=float(np.median(H[bs])), sat=float(np.median(S[bs])), lmax=float(L[bs].max()))
    if ring.sum() > 50:
        out.update(spill=float(L[ring].mean() - Lp[ring].mean()), ring=int(ring.sum()))
    out['fx_sat'] = float(S[fx].mean()) if fx.any() else 0.0
    out['mass_sat'] = float(S[m].mean()) if m.any() else 0.0
    if not quiet:
        print('--- %s' % tag)
        print('  mass %7d px (%.2f cells2)  core %7d' % (out['mass'], out['mass'] / CELL_PX ** 2, out['core']))
        if out['core'] > 100:
            print('  CORE L: mean %5.1f  std %5.1f  p5-p95 %3.0f-%3.0f  spread %3.0f'
                  % (out.get('mean', 0), out['std'], out['p5'], out['p95'], out['spread']))
        print('  bright&sat %6d px = %.3f%% of frame   hue %3.0f  sat %.2f'
              % (out['bs_px'], out['bs_pct'], out['hue'], out['sat']))
        print('  spill dL %+.1f  (hot %d px)' % (out['spill'], int(hot.sum())))
    return out
