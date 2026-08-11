"""Shared configuration: the shipped r4 shockwave, and the round 5 candidate."""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

MAX_RADIUS = 4.3
DURATION = 0.55
QUAD_HALF = MAX_RADIUS * 1.42
CELL = 2.7

DUST_SHADOW = np.array([0.112, 0.101, 0.090])
DUST_LIT = np.array([0.660, 0.632, 0.590])
CLOD_SHADOW = np.array([0.126, 0.114, 0.102])
CLOD_LIT = np.array([0.672, 0.645, 0.604])
COOL_LIGHT = np.array([0.303, 0.719, 1.000])
KEY_TINT = 0.55


def lit_by(albedo, light, strength=KEY_TINT):
    mixed = albedo * (1.0 + strength * (light - 1.0))
    want = 0.2126 * albedo[0] + 0.7152 * albedo[1] + 0.0722 * albedo[2]
    got = max(0.2126 * mixed[0] + 0.7152 * mixed[1] + 0.0722 * mixed[2], 1e-4)
    return mixed * min(want / got, 1.0 / max(mixed.max(), 1e-4))


def timings(life=DURATION):
    front_travel = min(max(life * 0.26, 0.105), 0.155)
    return dict(front_travel=front_travel, front_life=front_travel + 0.19,
                rim_hold=front_travel + 0.05, rim_life=front_travel + 0.15,
                collar_delay=0.025, collar_travel=front_travel * 1.15,
                collar_life=front_travel * 1.15 + 0.2)


def frame(t, base):
    """The animation curves in ImpactShockwave.Animate, evaluated at time t."""
    tm = timings()
    ft, fl = tm['front_travel'], tm['front_life']
    travel = min(t / ft, 1.0)
    eased = 1 - (1 - travel) ** 3.2
    decay = min(max((t - ft) / (fl - ft), 0.0), 1.0)

    outer = (MAX_RADIUS * 0.07 + (MAX_RADIUS - MAX_RADIUS * 0.07) * eased) * (1 + 0.03 * decay)
    band_full = MAX_RADIUS * 0.34
    band = (MAX_RADIUS * 0.06 + (band_full - MAX_RADIUS * 0.06) * eased) * (1 - 0.55 * decay * decay)

    rim_w = min(0.72, MAX_RADIUS * 0.16)
    width = MAX_RADIUS * 0.06 + (rim_w - MAX_RADIUS * 0.06) * eased
    fade = 1 - min(max((t - tm['rim_hold']) / (tm['rim_life'] - tm['rim_hold']), 0.0), 1.0)

    collar_outer_full = MAX_RADIUS * 0.48
    collar_half = collar_outer_full * 1.42
    since = t - tm['collar_delay']
    if since <= 0:
        collar_r, collar_th, collar_erode, collar_op = 0.0, 0.1, 1.2, 0.0
    else:
        ct = min(since / tm['collar_travel'], 1.0)
        ce = 1 - (1 - ct) ** 2.6
        cd = min(max((since - tm['collar_travel']) / (tm['collar_life'] - tm['collar_travel']), 0.0), 1.0)
        co = MAX_RADIUS * 0.05 + (collar_outer_full - MAX_RADIUS * 0.05) * ce
        cb = (MAX_RADIUS * 0.04 + (collar_outer_full * 0.42 - MAX_RADIUS * 0.04) * ce) * (1 - 0.5 * cd * cd)
        collar_r, collar_th = co / collar_half, cb / co
        collar_erode = -0.35 + 1.37 * cd ** 1.5
        collar_op = 0.96

    # the mound clod: delay 0.15, quad 5.65 world at the median downwind roll
    clod_delay, clod_life, clod_quad = 0.15, 0.595, 5.647
    if t <= clod_delay:
        clod_half, clod_op, clod_erode = 1.0, 0.0, 1.2
    else:
        prog = min((t - clod_delay) / clod_life, 1.0)
        arrive = min(prog / 0.16, 1.0)
        size = clod_quad * (0.42 + 0.58 * arrive) * (1 - 0.34 * prog ** 1.4)
        clod_half, clod_op = size * 0.5, 0.97
        settle = min(max((prog - 0.28) / 0.72, 0.0), 1.0)
        clod_erode = -0.15 + 1.35 * settle ** 1.4

    return dict(base,
                radius=outer / QUAD_HALF, thickness=band / outer,
                erode=-0.35 + 1.37 * decay ** 1.7,
                crest_width=width / QUAD_HALF,
                intensity=(2.70 + 0.35 * eased) * fade ** 0.9,
                seed=12.0 + t * 0.9,
                collar_half=collar_half, collar_radius=collar_r, collar_thickness=collar_th,
                collar_erode=collar_erode, collar_opacity=collar_op,
                collar_seed_grain=12.0 * 1.7 - t * 1.1,
                clod_half=clod_half, clod_opacity=clod_op, clod_erode=clod_erode,
                core_radius=base['core_shape'](t)[0], core_opacity=base['core_shape'](t)[1],
                core_plateau=base['core_shape'](t)[2])


def no_core(t):
    return 0.0, 0.0, 0.0


R4 = dict(
    legacy=True,
    shape_seed=17.3, collar_seed=63.0, lean_deg=25.0, arc_seed=110.0, opacity=1.0,
    collar_centre=(MAX_RADIUS * 0.12 * np.cos(np.radians(25.0)),
                   MAX_RADIUS * 0.12 * np.sin(np.radians(25.0))),
    dust_dark=list(DUST_SHADOW), dust_lit=list(DUST_LIT),
    dust_keylit=list(lit_by(DUST_LIT, COOL_LIGHT)),
    clod_dark=list(CLOD_SHADOW), clod_lit=list(CLOD_LIT),
    clod_keylit=list(lit_by(CLOD_LIT, COOL_LIGHT)),
    clod_lump_scale=26.0, clod_seed=7.0,
    tint_reach=1.05, ambient=0.042, key=0.74, sky=0.16, micro=0.05,
    wall_relief=0.26, lump_relief=0.070, lump_scale=34.0, relief_amp=None,
    key_elev=18.0, key_gamma=1.25, key_wrap=0.0,
    glow_lin=[0.075, 0.475, 1.000], ember_lin=[0.105, 0.600, 1.000], core_lin=[0.60, 0.60, 0.60],
    glint_amount=1.0,
    rim_push=-0.30,
    slab_in=1.15, slab_hold=0.50, slab_cut=0.20, slab_soft=0.13, slab_out=0.09,
    slab_swing=0.16, streak=0.45, streak_cells=34.0,
    tail_in=1.15, tail_amp=0.42, tail_pow=1.5,
    crest_out=1.45, out_amp=0.36, out_pow=0.85,
    ground_amp=0.52, ground_reach=2.00, ground_pow=0.65,
    ember_share=0.22, core_share=0.07, arc_count=6, arc_span=0.076,
    relief_bite=0.42, relief_gamma=0.75,
    core_white=[0, 0, 0], core_hot=[0, 0, 0], core_hue=[0, 0, 0],
    core_mid=0.5, core_wobble=0.0, core_seed=3.0, core_shape=no_core,
    bloom=True,
)
