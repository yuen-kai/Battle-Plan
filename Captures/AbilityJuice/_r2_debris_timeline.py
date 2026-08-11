import numpy as np
from PIL import Image
import os, json

ROOT = os.path.dirname(os.path.abspath(__file__))
CELL = 199.0          # px per board cell (measured from seams)
WU_PER_CELL = 2.7
PX_PER_WU = CELL/WU_PER_CELL   # 73.7 px per world unit horizontally
FPS = 30.0
T0_FRAME = 12         # contact sheet: frame0 = -0.40s -> impact frame = 12

def load(p): return np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def sat(a):
    mx = a.max(-1); mn = a.min(-1)
    return np.where(mx>1e-5, (mx-mn)/np.maximum(mx,1e-5), 0.0)

def analyse(D, label):
    frames = sorted(f for f in os.listdir(D) if f.endswith('.jpg'))
    pre = np.stack([load(os.path.join(D,f)) for f in frames[:6]])
    post = np.stack([load(os.path.join(D,f)) for f in frames[-6:]])
    plate = np.median(np.concatenate([pre,post]), axis=0)
    pl = lum(plate)
    rows=[]
    for i,f in enumerate(frames):
        im = load(os.path.join(D,f))
        L = lum(im)
        d = np.abs(im-plate).max(-1)
        mask = d > 12
        n = int(mask.sum())
        t = (i-T0_FRAME)/FPS
        r = dict(i=i, t=round(t,3), px=n)
        if n > 40:
            ys,xs = np.nonzero(mask)
            r['x0'],r['x1'],r['y0'],r['y1'] = int(xs.min()),int(xs.max()),int(ys.min()),int(ys.max())
            r['w_cell'] = round((xs.max()-xs.min()+1)/CELL,2)
            r['h_cell'] = round((ys.max()-ys.min()+1)/CELL,2)
            r['area_cell2'] = round(n/(CELL*CELL),3)
            eL = L[mask]; eP = pl[mask]
            r['lum_p5']  = round(float(np.percentile(eL,5)),1)
            r['lum_p50'] = round(float(np.percentile(eL,50)),1)
            r['lum_p95'] = round(float(np.percentile(eL,95)),1)
            r['lum_min'] = round(float(eL.min()),1)
            r['lum_max'] = round(float(eL.max()),1)
            # dark occluding material: >=60 darker than the plate underneath
            dark = mask & (pl - L > 60)
            r['dark_px'] = int(dark.sum())
            r['dark_cell2'] = round(dark.sum()/(CELL*CELL),3)
            # truly opaque dark: absolute lum in 40..110
            op = mask & (L>=25) & (L<=110)
            r['op40_110_cell2'] = round(op.sum()/(CELL*CELL),3)
            # clipped pixels (all 3 channels >=250)
            clip = (im>=250).all(-1) & mask
            r['clip_px'] = int(clip.sum())
            # saturation of bright pixels
            s = sat(im)[mask]
            r['sat_p90'] = round(float(np.percentile(s,90)),3)
            r['cy'] = int(ys.mean()); r['cx'] = int(xs.mean())
        rows.append(r)
    return rows, plate

for D,label in [(os.path.join(ROOT,'shots/r2/debris'),'R2'), (os.path.join(ROOT,'shots/r1/debris'),'R1')]:
    rows,_ = analyse(D,label)
    print("="*100); print(label)
    print("  t      px    w_c  h_c  area   darkc2 op_c2  lmin lp5  lp50 lp95 lmax clip  sat90")
    for r in rows:
        if r['px']<=40:
            if abs(r['t'])<0.9: print(" %+.2f  %6d   -" % (r['t'], r['px']))
            continue
        print(" %+.2f  %6d  %.2f %.2f %.3f  %.3f  %.3f  %5.1f %5.1f %5.1f %5.1f %5.1f %5d  %.2f" % (
            r['t'], r['px'], r['w_cell'], r['h_cell'], r['area_cell2'], r['dark_cell2'], r['op40_110_cell2'],
            r['lum_min'], r['lum_p5'], r['lum_p50'], r['lum_p95'], r['lum_max'], r['clip_px'], r['sat_p90']))
    json.dump(rows, open(os.path.join(ROOT,'_r2_debris_%s.json'%label),'w'), indent=1)
