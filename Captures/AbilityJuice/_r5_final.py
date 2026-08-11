import numpy as np, sys, os
from PIL import Image
sys.path.insert(0, os.getcwd())
import _r5_sw_sim as S
from _r5_sw_cfg import R4, frame

SL=[0.879,0.283,0.0836,0.0214,0.0055]
R_CORE = 1.62
def shape(t, R=R_CORE, rise=0.055, h0=0.085, h1=0.185, fall=0.265):
    if t <= rise: return 0.0,0.0,0.30
    if t < h0:
        k=(t-rise)/(h0-rise); return R*(0.42+0.58*k), 1.0, 0.30
    if t <= h1:
        k=(t-h0)/(h1-h0); return R*(1.0+0.06*k), 1.0, 0.30-0.04*k
    k=min((t-h1)/(fall-h1),1.0)
    if k>=1: return 0.0,0.0,0.26
    return R*1.06*(1.0-0.86*k**1.3), 1.0, 0.26

FINAL = dict(R4, legacy=False, glint_amount=0.0, core_lin=[0,0,0], key_wrap=0.0,
             slope_amp=SL, key=0.95, sky=0.02, ambient=0.012,
             form_in=0.00, form_out=0.92, lump_share=0.40, lump_scale=26.0,
             lump_relief=0.55, key_gamma=1.0, wall_gamma=1.0,
             core_white=[3.6]*3, core_hot=[1.05,2.20,3.00], core_hue=[1.00,2.10,2.90],
             core_mid=0.58, core_wobble=0.16, core_seed=3.0, core_bite=0.20, core_bite_scale=26.0,
             skirt_colour=[0.075,0.475,1.0], skirt_reach=0.95, skirt_pow=1.8,
             core_shape=shape)

print('=== ROUND 5 CANDIDATE  [ALL NUMBERS SIMULATED] ===')
print('frame   t        >L240   >=250x3   Lmax  centreL   mass    g_p99   istd  b&s')
tiles={}
for idx in (13,14,15,16,17,18,19,21,30):
    t=(idx-12)/30.0
    r,p = S.report('', frame(t, FINAL), quiet=True)
    print('f%02d  %+.4f  %6.3f%%  %6.3f%% %6.1f %7.1f %7d %7.2f %6.1f %5.3f'
          % (idx, t, r['hot'], r['clip3'], r['lmax'], r['centreL'], r['mass'],
             r.get('g99',0), r.get('istd',0), r['bs']))
    tiles[idx]=p.astype(np.uint8)

print('\n--- r4 legacy for comparison [SIMULATED] ---')
for idx in (13,17,30):
    t=(idx-12)/30.0
    r,p = S.report('', frame(t, R4), quiet=True)
    print('f%02d  %+.4f  %6.3f%%  %6.3f%% %6.1f %7.1f %7d %7.2f %6.1f %5.3f'
          % (idx, t, r['hot'], r['clip3'], r['lmax'], r['centreL'], r['mass'],
             r.get('g99',0), r.get('istd',0), r['bs']))
    tiles[-idx]=p.astype(np.uint8)

gap=np.full((512,8,3),16,np.uint8)
new=np.concatenate([tiles[13],gap,tiles[17],gap,tiles[30]],axis=1)
old=np.concatenate([tiles[-13],gap,tiles[-17],gap,tiles[-30]],axis=1)
Image.fromarray(np.concatenate([old,np.full((10,new.shape[1],3),16,np.uint8),new],axis=0)).save('_r5_strip.png')
hold=np.concatenate([tiles[14],gap,tiles[15],gap,tiles[16],gap,tiles[17],gap,tiles[18]],axis=1)
Image.fromarray(hold).save('_r5_hold.png')
print('\nwrote _r5_strip.png (top r4 sim, bottom r5 sim) and _r5_hold.png (f14..f18)')
