import numpy as np, sys, os
from PIL import Image
from scipy.ndimage import binary_erosion
sys.path.insert(0, os.getcwd())
import _r5_sw_sim as S
from _r5_sw_cfg import R4, frame

def grad_map(cfg, tag):
    px, plate, dust_a, core_a = S.compose(cfg)
    p = S.to_panel(px); pl = S.to_panel(plate)
    L,_,_ = S.LSH(p); Lp,_,_ = S.LSH(pl)
    changed = np.abs(p-pl).max(-1) > 20
    mass = changed & (L < Lp-25)
    interior = binary_erosion(mass, np.ones((9,9)))
    gy,gx = np.gradient(L); g = np.hypot(gy,gx)
    gi = g[interior]
    print('%-34s interior %6d  p50 %5.2f p90 %5.2f p99 %6.2f max %6.1f  std %.1f spread %.0f'
          % (tag, interior.sum(), np.percentile(gi,50), np.percentile(gi,90),
             np.percentile(gi,99), gi.max(), L[interior].std(),
             np.percentile(L[interior],95)-np.percentile(L[interior],5)))
    return interior, g, p

base = frame(0.1667, dict(R4, legacy=False))
print('=== which layer owns the steep interior gradient? ===')
grad_map(base, 'r5 shading, everything')
grad_map(dict(base, bloom=False), 'no bloom')
grad_map(dict(base, intensity=0.0), 'no crest')
grad_map(dict(base, clod_opacity=0.0), 'no mound clod')
grad_map(dict(base, collar_opacity=0.0), 'no collar')
grad_map(dict(base, intensity=0.0, clod_opacity=0.0, collar_opacity=0.0, bloom=False),
         'front dust alone')
interior, g, p = grad_map(dict(base, intensity=0.0, clod_opacity=0.0, collar_opacity=0.0,
                               bloom=False, glint_amount=0.0), 'front dust alone (no glint)')
vis = p.astype(np.uint8).copy()
hot = interior & (g > 12)
vis[hot] = [255,0,0]
Image.fromarray(vis).save('_r5_diag_grad.png')
print('wrote _r5_diag_grad.png (red = interior gradient > 12/px)')
