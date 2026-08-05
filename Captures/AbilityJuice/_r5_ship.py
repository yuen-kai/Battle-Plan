"""Final round 5 prediction, at exactly the values ImpactShockwave.cs now ships.

EVERY NUMBER PRINTED HERE IS SIMULATED. The simulator is validated against the real r4 frames
in the block at the bottom: where it disagrees with the capture, the capture wins.
"""
import numpy as np, sys, os
from PIL import Image
sys.path.insert(0, os.getcwd())
import _r5_sw_sim as S
from _r5_sw_cfg import R4, frame

CELL = 2.7
MAXR = 4.3
CORE_R = min(CELL * 0.66, MAXR * 0.400)          # 1.720 world
GLOW_REACH = min(CORE_R * 0.6, MAXR * 0.34)      # 1.032 world
FRONT_TRAVEL = min(max(0.55 * 0.26, 0.105), 0.155)
RISE, H0, H1, LIFE = (FRONT_TRAVEL * k for k in (0.39, 0.60, 1.30, 1.86))

def shape(t):
    if t < RISE or t >= LIFE:
        return 0.0, 0.0, 0.30
    if t < H0:
        k = (t - RISE) / (H0 - RISE)
        return CORE_R * (0.42 + 0.58 * k), 1.0, 0.30
    if t <= H1:
        k = (t - H0) / (H1 - H0)
        return CORE_R * (1.0 + 0.06 * k), 1.0, 0.30 - 0.04 * k
    k = (t - H1) / (LIFE - H1)
    return CORE_R * 1.06 * (1.0 - 0.86 * k ** 1.3), 1.0, 0.26

SHIP = dict(R4, legacy=False, glint_amount=0.0, core_lin=[0, 0, 0], key_wrap=0.0,
            slope_amp=[0.879, 0.283, 0.0836, 0.0214, 0.0055],
            lump_scale=26.0, lump_relief=0.55, lump_share=0.40,
            form_in=0.0, form_out=0.92, key=0.95, key_gamma=1.0,
            ambient=0.012, sky=0.02, wall_gamma=1.0, clod_lump_scale=23.0,
            core_white=[3.6] * 3, core_hot=[1.05, 2.20, 3.00], core_hue=[1.00, 2.10, 2.90],
            core_mid=0.58, core_wobble=0.16, core_bite=0.20, core_bite_scale=26.0, core_seed=12.0,
            skirt_colour=[0.075, 0.475, 1.0], skirt_intensity=1.35,
            skirt_reach=GLOW_REACH, skirt_pow=1.8, core_shape=shape)

print('core radius %.3f world = %.2f cells across   hold %.3f-%.3f s (f%.1f-f%.1f)'
      % (CORE_R, 2 * CORE_R / CELL, H0, H1, 12 + H0 * 30, 12 + H1 * 30))
print('\n=== ROUND 5 AS SHIPPED  [SIMULATED] ===')
print('frame    t        >L240   >=250all3   Lmax  centreL   grad p99  int.std   b&s')
tiles = {}
for idx in (13, 15, 16, 17, 19, 30):
    t = (idx - 12) / 30.0
    r, p = S.report('', frame(t, SHIP), quiet=True)
    print('f%02d   %+.4f  %6.3f%%   %6.3f%%  %6.1f %7.1f   %7.2f  %6.1f  %5.3f'
          % (idx, t, r['hot'], r['clip3'], r['lmax'], r['centreL'],
             r.get('g99', 0), r.get('istd', 0), r['bs']))
    tiles[idx] = p.astype(np.uint8)

print('\n=== SIMULATOR VALIDATION: same code, r4 parameters, vs the real r4 capture ===')
print('                       >L240   >=250all3   Lmax  centreL   grad p99  int.std   b&s')
for idx in (13, 17):
    r, _ = S.report('', frame((idx - 12) / 30.0, R4), quiet=True)
    print('f%02d simulated       %6.3f%%   %6.3f%%  %6.1f %7.1f   %7.2f  %6.1f  %5.3f'
          % (idx, r['hot'], r['clip3'], r['lmax'], r['centreL'],
             r.get('g99', 0), r.get('istd', 0), r['bs']))
print('f13 REAL CAPTURE      0.042%%    0.000%%   247.1   169.4     34.30    31.4      -')
print('f17 REAL CAPTURE      0.080%%    0.000%%   249.0    92.4     42.69    44.5   0.353')

gap = np.full((512, 8, 3), 16, np.uint8)
Image.fromarray(np.concatenate([tiles[13], gap, tiles[17], gap, tiles[30]], axis=1)) \
    .save('_r5_ship_strip.png')
Image.fromarray(np.concatenate([tiles[15], gap, tiles[16], gap, tiles[17], gap, tiles[19]], axis=1)) \
    .save('_r5_ship_hold.png')
print('\nwrote _r5_ship_strip.png and _r5_ship_hold.png')

# ---- strip-level aggregates, and the local-contrast check
from scipy.ndimage import binary_erosion, uniform_filter
def local9(L, m):
    a = uniform_filter(L, 9); b = uniform_filter(L*L, 9)
    return np.sqrt(np.maximum(b - a*a, 0))[m]

print('\n=== STRIP-LEVEL (mean over the three panels the critic sees) [SIMULATED] ===')
for label, cfg in (('r4 shipped', R4), ('r5 candidate', SHIP)):
    hot = clip = bs = 0.0
    for idx in (13, 17, 30):
        r, _ = S.report('', frame((idx - 12) / 30.0, cfg), quiet=True)
        hot += r['hot']; clip += r['clip3']; bs += r['bs']
    print('  %-14s >L240 %.3f%%   clipped %.3f%%   bright&saturated %.3f%%'
          % (label, hot / 3, clip / 3, bs / 3))
print('  r4 REAL strip: >L240 0.041%  clipped 0.000%  bright&saturated 0.353%')

print('\n=== local 9x9 contrast inside the dust, f17 [SIMULATED except the two real rows] ===')
for label, cfg in (('r4 simulated', R4), ('r5 simulated', SHIP)):
    _, p = S.report('', frame(0.1667, cfg), quiet=True)
    L, _, _ = S.LSH(p)
    m = binary_erosion((np.abs(p - p[0, 0]).max(-1) > 20) & (L < 149.6), np.ones((9, 9)))
    lc = local9(L, m)
    print('  %-14s mean %5.2f  p90 %5.2f' % (label, lc.mean(), np.percentile(lc, 90)))
print('  r4 REAL        mean 13.37  p90 32.31   (that peak IS the terminator)')
print('  Clash Mini ref mean  6.45 / 14.42')
