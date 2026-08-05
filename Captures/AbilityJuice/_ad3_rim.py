import numpy as np, os
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

def parts(rnd, idx):
    pl = plate(rnd,'numbers'); fr = load(fp(rnd,'numbers',idx))
    L,S,H = lum(fr), sat(fr), hue(fr)
    ch = np.abs(fr-pl).max(-1) > 20
    ch[int(ch.shape[0]*0.60):,:] = False
    amber = ch & (S>0.40) & (H>25) & (H<75) & (L>90)
    dark  = ch & (L<70)
    solid = ndimage.binary_fill_holes(amber|dark)
    lab,n = ndimage.label(solid); sizes = ndimage.sum(solid,lab,range(1,n+1))
    solid = lab == int(np.argmax(sizes))+1
    bright = solid & (L>100)
    lab2,n2 = ndimage.label(bright)
    rim = np.zeros_like(bright); dg = np.zeros_like(bright)
    for k in range(1,n2+1):
        c = lab2==k; a=c.sum()
        if a<400: continue
        ys,xs=np.where(c); f=a/((xs.max()-xs.min()+1)*(ys.max()-ys.min()+1))
        (rim if f<0.30 else dg).__ior__(c)
    return fr, L, S, H, solid, rim, dg

for rnd, idx in (('r3',14),('r3',20),('r2',20)):
    fr,L,S,H,solid,rim,dg = parts(rnd,idx)
    ys,xs = np.where(solid); h = ys.max()-ys.min()+1
    # rim thickness: for each rim pixel, distance to nearest non-rim within solid
    dt = ndimage.distance_transform_edt(rim)
    # skeleton-ish: thickness ~ 2*max local distance along the ring
    # sample thickness by scanning rows/cols across the ring
    print('== %s f%03d  badge h=%d' % (rnd, idx, h))
    print('   rim px=%d  digit px=%d  rim/digit=%.2f' % (rim.sum(), dg.sum(), rim.sum()/max(1,dg.sum())))
    # min separation between digit ink and rim
    dtr = ndimage.distance_transform_edt(~rim)
    sep = dtr[dg]
    print('   digit->rim separation: min=%.1f p1=%.1f p5=%.1f median=%.1f px'
          % (sep.min(), np.percentile(sep,1), np.percentile(sep,5), np.median(sep)))
    # rim continuity: thickness along the top edge, sampled per column
    ths=[]
    for x in range(xs.min(), xs.max()+1):
        col = np.where(rim[:,x])[0]
        if len(col)==0: continue
        # top run
        run=1
        for i in range(1,len(col)):
            if col[i]==col[i-1]+1: run+=1
            else: break
        ths.append(run)
    ths=np.array(ths)
    print('   top-rim thickness per column: n=%d min=%d p5=%.0f median=%.0f max=%d'
          % (len(ths), ths.min(), np.percentile(ths,5), np.median(ths), ths.max()))
    # thickness normalised to badge height
    print('   rim thickness / badge height = %.3f ; digit-rim gap / badge height = %.3f'
          % (np.median(ths)/h, np.median(sep)/h))
