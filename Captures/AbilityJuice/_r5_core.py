import numpy as np, sys, os, itertools
from PIL import Image
sys.path.insert(0, os.getcwd())
import _r5_sw_sim as S
from _r5_sw_cfg import R4, frame

SL=[0.879,0.283,0.0836,0.0214,0.0055]
DUST = dict(R4, legacy=False, glint_amount=0.0, core_lin=[0,0,0], key_wrap=0.0,
            slope_amp=SL, key=0.95, sky=0.02, ambient=0.012,
            form_in=0.00, form_out=0.92, lump_share=0.40, lump_scale=26.0,
            lump_relief=0.55, key_gamma=1.0, wall_gamma=1.0,
            core_white=[5,5,5], core_hot=[1.05,2.20,3.00],
            core_hue=[1.05,2.20,3.00], core_mid=1.0, core_wobble=0.09, core_seed=3.0,
            skirt_colour=[0.075,0.475,1.0], skirt_reach=0.9, skirt_pow=1.8)

def core_shape(R, plateau, hold=(0.085,0.185), rise=0.055, fall=0.265):
    def f(t):
        if t <= rise: k = 0.0
        elif t < hold[0]: k = (t-rise)/(hold[0]-rise)
        elif t <= hold[1]: k = 1.0
        else: k = max(0.0, 1.0-(t-hold[1])/(fall-hold[1]))
        # collapses rather than fades: the radius goes, the radiance does not
        return R*(0.35+0.65*min(k*1.6,1.0)) if k>0 else 0.0, 1.0 if k>0 else 0.0, plateau
    return f

print('%-30s %7s %8s %7s %7s %6s %6s' % ('R(cells)/plateau/skirt','>L240','>=250x3','Lmax','centreL','b&s','p99'))
for R, plateau, sk in itertools.product([1.40,1.55,1.75],[0.36,0.46],[0.0,0.9]):
    cfg = dict(DUST, core_shape=core_shape(R, plateau), skirt_reach=sk)
    r,_ = S.report('', frame(0.1667, cfg), quiet=True)
    print('%-30s %6.3f%% %7.3f%% %7.1f %7.1f %6.3f %6.2f'
          % ('%.2f cells / %.2f / %.1f'%(2*R/2.7, plateau, sk), r['hot'], r['clip3'],
             r['lmax'], r['centreL'], r['bs'], r.get('g99',0)))
