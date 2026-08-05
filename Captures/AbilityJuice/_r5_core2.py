import numpy as np, sys, os, itertools
from PIL import Image
sys.path.insert(0, os.getcwd())
import _r5_sw_sim as S
from _r5_sw_cfg import R4, frame
SL=[0.879,0.283,0.0836,0.0214,0.0055]
BASE = dict(R4, legacy=False, glint_amount=0.0, core_lin=[0,0,0], key_wrap=0.0,
            slope_amp=SL, key=0.95, sky=0.02, ambient=0.012,
            form_in=0.00, form_out=0.92, lump_share=0.40, lump_scale=26.0,
            lump_relief=0.55, key_gamma=1.0, wall_gamma=1.0,
            core_wobble=0.09, core_seed=3.0,
            skirt_colour=[0.075,0.475,1.0], skirt_reach=0.95, skirt_pow=1.8)

def shape(R, plateau, hold=(0.085,0.185), rise=0.055, fall=0.265):
    def f(t):
        if t <= rise: return 0.0,0.0,plateau
        if t < hold[0]: k=(t-rise)/(hold[0]-rise)
        elif t <= hold[1]: k=1.0
        else: k=max(0.0,1.0-(t-hold[1])/(fall-hold[1]))
        if k<=0: return 0.0,0.0,plateau
        return R*(0.35+0.65*min(k*1.6,1.0)), 1.0, plateau
    return f

print('%-44s %7s %8s %7s %6s' % ('R cells / white / plateau / mid / rim','>L240','>=250x3','centreL','b&s'))
for R, w, pl, mid, rim in itertools.product(
        [1.50,1.62,1.75],[3.2,4.0],[0.28],[0.58],[(1.00,2.10,2.90)]):
    cfg = dict(BASE, core_white=[w]*3, core_hot=[1.05,2.20,3.00], core_hue=list(rim),
               core_mid=mid, core_shape=shape(R, pl))
    r,_ = S.report('', frame(0.1667, cfg), quiet=True)
    print('%-44s %6.3f%% %7.3f%% %7.1f %6.3f'
          % ('%.2f / %.1f / %.2f / %.2f / %s'%(2*R/2.7,w,pl,mid,rim), r['hot'], r['clip3'],
             r['centreL'], r['bs']))
