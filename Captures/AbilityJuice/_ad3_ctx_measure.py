import numpy as np, os, json
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

def find_badge(fr, win, minarea=800):
    """badge = filled region whose border is a hard dark keyline, inside win=(x0,y0,x1,y1)"""
    x0,y0,x1,y1 = win
    sub = fr[y0:y1, x0:x1]
    L = lum(sub)
    dark = L < 55
    solid = ndimage.binary_fill_holes(ndimage.binary_closing(dark, np.ones((5,5))))
    lab,n = ndimage.label(solid)
    best=None
    for k in range(1,n+1):
        c = lab==k; a=int(c.sum())
        if a<minarea: continue
        ys,xs=np.where(c)
        w,h = xs.max()-xs.min()+1, ys.max()-ys.min()+1
        if h<20 or w<20: continue
        if a/(w*h) < 0.55: continue
        if best is None or a>best['area']:
            best=dict(area=a,x0=int(xs.min()+x0),x1=int(xs.max()+x0),y0=int(ys.min()+y0),y1=int(ys.max()+y0),
                      w=int(w),h=int(h),fill=round(a/(w*h),2), mask=c)
    return best

print('=== POGO: badge bbox per frame (win = right half) ===')
for i in range(44, 80, 2):
    fr = load(fp('r3','pogo',i))
    b = find_badge(fr, (700,300,1024,520))
    if b:
        clipped = 'CLIPPED@x=1023' if b['x1'] >= 1022 else ''
        L,S,H = lum(fr), sat(fr), hue(fr)
        m = np.zeros(fr.shape[:2], bool); m[b['y0']:b['y1']+1, b['x0']:b['x1']+1] = True
        print('  f%03d  bbox %dx%d @(%d,%d)-(%d,%d)  %s' % (i,b['w'],b['h'],b['x0'],b['y0'],b['x1'],b['y1'],clipped))

print()
print('=== POGO badge colour at f058 vs floor ===')
fr = load(fp('r3','pogo',58)); L,S,H = lum(fr),sat(fr),hue(fr)
b = find_badge(fr,(700,300,1024,520))
if b:
    reg = (slice(b['y0'],b['y1']+1), slice(b['x0'],b['x1']+1))
    Lr, Sr = L[reg], S[reg]
    print('  badge box L: p5=%.0f p50=%.0f p95=%.0f  max=%.0f ; S p50=%.2f p95=%.2f'
          % (np.percentile(Lr,5),np.percentile(Lr,50),np.percentile(Lr,95),Lr.max(),
             np.percentile(Sr,50),np.percentile(Sr,95)))
plp = plate('r3','pogo'); Lp=lum(plp)
print('  pogo floor L p50 = %.0f' % np.median(Lp))

print()
print('=== DEATH: badge bbox / colour per frame ===')
for i in range(10, 44, 3):
    fr = load(fp('r3','death',i))
    b = find_badge(fr, (300,80,1024,480))
    if b:
        L,S,H = lum(fr),sat(fr),hue(fr)
        reg=(slice(b['y0'],b['y1']+1), slice(b['x0'],b['x1']+1))
        print('  f%03d bbox %dx%d @(%d,%d) L p50=%.0f p95=%.0f  S p50=%.2f p95=%.2f'
              % (i,b['w'],b['h'],b['x0'],b['y0'], np.percentile(L[reg],50), np.percentile(L[reg],95),
                 np.percentile(S[reg],50), np.percentile(S[reg],95)))
