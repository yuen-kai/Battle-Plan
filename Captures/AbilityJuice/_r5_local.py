import numpy as np, sys, os, itertools
from PIL import Image
from scipy.ndimage import binary_erosion, uniform_filter
sys.path.insert(0, os.getcwd())
import _r5_sw_sim as S
from _r5_sw_cfg import R4, frame
import _r5_ship as SH

def local_contrast(L, mask, k=9):
    m = uniform_filter(L, k); m2 = uniform_filter(L*L, k)
    v = np.maximum(m2 - m*m, 0)
    return np.sqrt(v)[mask]

def stats(p, plate_L, tag):
    L,_,_ = S.LSH(p)
    mass = (np.abs(p - p[0,0]).max(-1) > 20) & (L < plate_L - 25)
    inte = binary_erosion(mass, np.ones((9,9)))
    if inte.sum() < 200: return
    lc = local_contrast(L, inte)
    gy,gx = np.gradient(L); g = np.hypot(gy,gx)[inte]
    print('%-28s  local9 mean %5.2f p90 %5.2f | grad p99 %6.2f | std %5.1f'
          % (tag, lc.mean(), np.percentile(lc,90), np.percentile(g,99), L[inte].std()))

# real r4
real = np.asarray(Image.open('shots/r4/shockwave/f0017.jpg').convert('RGB').resize((512,512), Image.LANCZOS), dtype=np.float64)
plate = np.asarray(Image.open('shots/r4/shockwave/f0002.jpg').convert('RGB').resize((512,512), Image.LANCZOS), dtype=np.float64)
Lp,_,_ = S.LSH(plate)
Lr,_,_ = S.LSH(real)
massr = (np.abs(real-plate).max(-1) > 20) & (Lr < Lp - 25)
inter = binary_erosion(massr, np.ones((9,9)))
lc = local_contrast(Lr, inter)
print('%-28s  local9 mean %5.2f p90 %5.2f' % ('r4 REAL CAPTURE', lc.mean(), np.percentile(lc,90)))

V = {'A':[0.879,0.283,0.0836,0.0214,0.0055],
     'E':[0.170,0.423,0.275,0.0620,0.0107],
     'F':[0.10,0.30,0.42,0.20,0.05],
     'G':[0.06,0.20,0.40,0.30,0.10]}
for name, scale, share in itertools.product(['A','E','F','G'],[26.0,40.0],[0.55]):
    cfg = dict(SH.SHIP, slope_amp=V[name], lump_scale=scale, lump_share=share, lump_relief=0.8)
    _,p = S.report('', frame(0.1667, cfg), quiet=True)
    stats(p, 174.6, '%s scale %.0f share %.2f'%(name,scale,share))

# reference
for n in ('cm-8newabilities-13-impact.jpg','cm-everyability-05-impact.jpg'):
    a = np.asarray(Image.open('reference/impact/'+n).convert('RGB'), dtype=np.float64)
    L,_,_ = S.LSH(a)
    m = (L > 30) & (L < 150)
    if m.sum() > 500:
        lc = local_contrast(L, m)
        print('%-28s  local9 mean %5.2f p90 %5.2f' % ('REF '+n[:18], lc.mean(), np.percentile(lc,90)))
