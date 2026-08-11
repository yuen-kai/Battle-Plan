import numpy as np, os
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

fr = load(fp('r3','pogo',58)); L,S,H = lum(fr),sat(fr),hue(fr)
# tight badge body only (above the health bar)
for box in [(963,378,1023,416)]:
    x0,y0,x1,y1 = box
    Ls, Ss, Hs = L[y0:y1,x0:x1], S[y0:y1,x0:x1], H[y0:y1,x0:x1]
    print('tight pogo badge', box)
    print('  L p5=%.0f p50=%.0f p95=%.0f | S p50=%.3f p90=%.3f | frac S>0.45 = %.3f'
          % (np.percentile(Ls,5),np.percentile(Ls,50),np.percentile(Ls,95),
             np.percentile(Ss,50),np.percentile(Ss,90),(Ss>0.45).mean()))
    body = Ls<70; digit = Ls>170
    print('  body px=%d  L p50=%.0f S p50=%.2f' % (body.sum(), np.median(Ls[body]), np.median(Ss[body])))
    print('  digit px=%d L p50=%.0f S p50=%.2f' % (digit.sum(), np.median(Ls[digit]), np.median(Ss[digit])))

# health-bar occlusion: green bar row profile with and without badge
print()
print('green health-bar continuity at f058 (y 418..436):')
for y in (420, 424, 428, 432):
    row = fr[y, 880:1024]
    Sr, Hr, Lr = sat(row), hue(row), lum(row)
    g = (Sr>0.30)&(Hr>90)&(Hr<170)
    xs = np.where(g)[0]
    print('  y=%d green x %s..%s (n=%d)   dark px in 950..1024: %d'
          % (y, (880+xs.min()) if len(xs) else '-', (880+xs.max()) if len(xs) else '-', len(xs),
             int((lum(fr[y,950:1024])<60).sum())))

print()
print('same rows at f050 (no badge yet):')
fr0 = load(fp('r3','pogo',50))
for y in (420, 424, 428, 432):
    row = fr0[y, 880:1024]
    Sr, Hr = sat(row), hue(row)
    g = (Sr>0.30)&(Hr>90)&(Hr<170)
    xs = np.where(g)[0]
    print('  y=%d green x %s..%s (n=%d)' % (y, (880+xs.min()) if len(xs) else '-', (880+xs.max()) if len(xs) else '-', len(xs)))
