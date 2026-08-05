import numpy as np, sys, os
from PIL import Image
from scipy.ndimage import binary_erosion, binary_dilation
sys.path.insert(0, os.getcwd())
import _r5_sw_sim as S
from _r5_sw_cfg import R4, frame
SL = {'A':[0.879,0.283,0.0836,0.0214,0.0055],'C':[1.244,0.218,0.0331,0.0,0.0]}
R5 = dict(R4, legacy=False, glint_amount=0.0, core_lin=[0,0,0], key_wrap=0.0,
          slope_amp=SL['A'], lump_scale=18.0, lump_relief=0.8, lump_share=0.55,
          wall_gamma=1.15, key_gamma=1.0, key=0.95, sky=0.045, ambient=0.022)
def go(cfg, tag):
    px, plate, da, ca = S.compose(cfg)
    p = S.to_panel(px); pl = S.to_panel(plate)
    L,_,_ = S.LSH(p); Lp,_,_ = S.LSH(pl)
    mass = (np.abs(p-pl).max(-1)>20) & (L < Lp-25)
    inte = binary_erosion(mass, np.ones((9,9)))
    gy,gx = np.gradient(L); g=np.hypot(gy,gx); gi=g[inte]; li=L[inte]
    print('%-30s p50 %5.2f p90 %5.2f p99 %6.2f | std %5.1f p5 %3.0f p95 %3.0f spread %3.0f'
          % (tag, np.percentile(gi,50), np.percentile(gi,90), np.percentile(gi,99),
             li.std(), np.percentile(li,5), np.percentile(li,95),
             np.percentile(li,95)-np.percentile(li,5)))
    return inte, g, p
b = frame(0.1667, R5)
go(b, 'all layers')
go(dict(b, clod_opacity=0.0), 'no clod')
go(dict(b, collar_opacity=0.0), 'no collar')
go(dict(b, intensity=0.0), 'no crest')
inte,g,p = go(dict(b, clod_opacity=0.0, collar_opacity=0.0, intensity=0.0, bloom=False), 'front dust alone')
vis=p.astype(np.uint8).copy(); vis[inte & (g>10)]=[255,0,0]
Image.fromarray(vis).save('_r5_diag2_alone.png')
inte,g,p = go(b, 'all layers (map)')
vis=p.astype(np.uint8).copy(); vis[inte & (g>10)]=[255,0,0]
Image.fromarray(vis).save('_r5_diag2_all.png')
