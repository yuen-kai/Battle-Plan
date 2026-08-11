import numpy as np, sys, os, itertools, colorsys
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _b4_sw_pipe import s2l
from _b4_sw_sim import render, metrics

QUAD_HALF = 4.3 * 1.42
W_PX = 0.688 * 72.0


def frame(t, cfg):
    travel = min(t / 0.143, 1.0)
    eased = 1 - (1 - travel) ** 3.2
    decay = min(max((t - 0.143) / 0.19, 0), 1)
    outer = (4.3 * 0.07 + (4.3 - 4.3 * 0.07) * eased) * (1 + 0.03 * decay)
    band = (4.3 * 0.06 + (4.3 * 0.34 - 4.3 * 0.06) * eased) * (1 - 0.55 * decay * decay)
    width = 4.3 * 0.06 + (0.688 - 4.3 * 0.06) * eased
    fade = 1 - min(max((t - 0.193) / 0.10, 0), 1)
    return dict(cfg, radius=outer / QUAD_HALF, thickness=band / outer,
                erode=-0.35 + 1.37 * decay ** 1.7, crest_width=width / QUAD_HALF,
                intensity=(2.70 + 0.35 * eased) * fade ** 0.9, seed=12.0 + t * 0.9)


FINAL = dict(
    legacy=False, shape_seed=17.3, lean_deg=25.0, arc_seed=110.0, opacity=1.0,
    dust_dark=[0.112, 0.101, 0.090], dust_lit=[0.660, 0.632, 0.590],
    dust_keylit=[0.5075, 0.6626, 0.7369], tint_reach=1.05,
    ambient=0.042, key=0.74, sky=0.16, micro=0.05, wall_ao=0.0,
    wall_relief=0.26, lump_relief=0.070, lump_scale=34.0, grad_px=1.6,
    key_elev=18.0, key_gamma=1.25,
    glow_lin=[0.075, 0.475, 1.000], ember_lin=[0.105, 0.600, 1.000], core_lin=[0.60, 0.60, 0.60],
    rim_push=-0.30, peak=3.05,
    slab_in=1.15, slab_hold=0.50, slab_cut=0.20, slab_soft=0.13, slab_out=0.09,
    slab_swing=0.16, streak=0.45, streak_cells=34.0,
    tail_in=1.15, tail_amp=0.42, tail_pow=1.5,
    crest_out=1.45, out_amp=0.36, out_pow=0.85,
    ground_amp=0.52, ground_reach=2.00, ground_pow=0.65,
    ember_share=0.22, core_share=0.07, arc_count=5, arc_span=0.076,
    relief_bite=0.42, relief_gamma=0.75,
)

if os.environ.get('SWEEP'):
    print('=== ground glow sweep at the final look ===')
    for ga, gr, gp in itertools.product([0.34, 0.44, 0.54, 0.66], [1.5, 2.1, 2.8], [0.6, 0.9]):
        c = frame(0.1667, dict(FINAL, ground_amp=ga, ground_reach=gr, ground_pow=gp))
        p, pl = render(c)
        r = metrics(p, pl, '', quiet=True)
        print('  amp %.2f reach %.2f (%3.0f px) pow %.1f -> spill %+5.1f ring %6d  b&s %5d  fxsat %.3f'
              % (ga, gr, gr * W_PX, gp, r['spill'], r.get('ring', 0), r['bs_px'], r['fx_sat']))
    sys.exit()

hsv = colorsys.hsv_to_rgb(233.9 / 360, 0.88, 1.0)
LEG = dict(FINAL, legacy=True, dust_dark=[0.272, 0.260, 0.247], dust_lit=[0.470, 0.450, 0.420],
           glow_lin=list(s2l(np.array(hsv))), ember_lin=[1.2, 0.56, 0.042], core_lin=[1, 1, 1],
           core_share=0.09, falloff=1.35, peak=3.1)

tiles, res = [], {}
for t, name in ((0.0333, 'f13 +0.033'), (0.1667, 'f17 +0.167'), (0.2333, 'f19 +0.233')):
    px, pl = render(frame(t, FINAL))
    print()
    r = metrics(px, pl, 'NEW  %s' % name)
    print('  mean sat: footprint %.3f  mass %.3f' % (r['fx_sat'], r['mass_sat']))
    res[name] = r
    tiles.append(px.astype(np.uint8))
    if t == 0.1667:
        lpx, lpl = render(frame(t, LEG))
        lr = metrics(lpx, lpl, 'r3   %s' % name)
        print('  mean sat: footprint %.3f  mass %.3f' % (lr['fx_sat'], lr['mass_sat']))
        legacy_tile = lpx.astype(np.uint8)
        res['r3'] = lr

strip = np.concatenate([legacy_tile] + tiles, axis=1)
Image.fromarray(strip).resize((strip.shape[1] // 2, strip.shape[0] // 2), Image.LANCZOS) \
    .save('Captures/AbilityJuice/_b4_sw_look.png')
sm = Image.fromarray(strip).resize((strip.shape[1] // 8, strip.shape[0] // 8), Image.LANCZOS)
sm.resize((strip.shape[1] // 2, strip.shape[0] // 2), Image.NEAREST).save('Captures/AbilityJuice/_b4_sw_squint.png')

# what the sim numbers imply for the shipped strip, using the r3 sim-vs-real calibration
SIM2REAL_BS = 1574.0 / 741.0
PANEL = 0.2524
tot = (res['f13 +0.033']['bs_px'] + res['f17 +0.167']['bs_px']) * SIM2REAL_BS * PANEL
print('\n=== predicted shipped strip ===')
print('  r3 measured strip bright&sat: 538 px = 0.068%')
print('  predicted (f13+f17, aftermath frame contributes ~0): %.0f px = %.2f%% of the strip'
      % (tot, 100.0 * tot / (1552 * 512)))
