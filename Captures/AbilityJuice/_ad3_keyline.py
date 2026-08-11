import numpy as np, os
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

def solid_of(fr, pl, ybot=0.60, thr=20):
    L,S,H = lum(fr), sat(fr), hue(fr)
    ch = np.abs(fr-pl).max(-1) > thr
    ch[int(ch.shape[0]*ybot):,:] = False
    amber = ch & (S>0.40) & (H>25) & (H<75) & (L>90)
    dark  = ch & (L<70)
    s = ndimage.binary_fill_holes(amber|dark)
    lab,n = ndimage.label(s)
    sizes = ndimage.sum(s, lab, range(1,n+1))
    return lab == int(np.argmax(sizes))+1, L

def keyline(fr, pl, ring_px=3):
    solid, L = solid_of(fr, pl)
    er = ndimage.binary_erosion(solid, np.ones((3,3)), iterations=1)
    band = solid & ~ndimage.binary_erosion(solid, np.ones((3,3)), iterations=ring_px)
    vals = L[band]
    return dict(n=int(band.sum()), pct_below60=float((vals<60).mean()*100),
                p50=float(np.median(vals)), p90=float(np.percentile(vals,90)),
                mean=float(vals.mean()))

def occlusion(rnd, idxs):
    pl = plate(rnd,'numbers')
    # unit + health bar region from the clean plate
    Lp, Sp, Hp = lum(pl), sat(pl), hue(pl)
    unit = ((Sp>0.28)&(((Hp>320)|(Hp<25))|((Hp>90)&(Hp<170)))) | (Lp<50)
    unit[:380,:]=False; unit[600:,:]=False
    unit[:, :430]=False; unit[:, 780:]=False
    unit = ndimage.binary_closing(unit, np.ones((5,5)))
    lab,n = ndimage.label(unit)
    sizes = ndimage.sum(unit, lab, range(1,n+1))
    unit = lab == int(np.argmax(sizes))+1
    unit = ndimage.binary_fill_holes(unit)
    ys,xs = np.where(unit)
    out=[]
    for i in idxs:
        fr = load(fp(rnd,'numbers',i))
        d = np.abs(fr-pl).max(-1)
        out.append((i, int((d[unit]>6).sum()), float(d[unit].max()), int(unit.sum())))
    return out, (int(ys.min()),int(ys.max()),int(xs.min()),int(xs.max()))

if __name__=='__main__':
    for rnd in ('r2','r3'):
        pl = plate(rnd,'numbers')
        print('==',rnd,'keyline (outer 3px ring of the badge silhouette)')
        for i in (13,14,15,18,20,24):
            fr = load(fp(rnd,'numbers',i))
            k = keyline(fr,pl)
            print('   f%03d n=%5d  %%<L60 = %5.1f%%   p50=%5.1f  p90=%5.1f' % (i,k['n'],k['pct_below60'],k['p50'],k['p90']))
        occ, bb = occlusion(rnd,(12,13,14,15,16,18,20,24,28))
        print('   unit+bar mask bbox y%d..%d x%d..%d  px=%d' % (bb[0],bb[1],bb[2],bb[3], occ[0][3]))
        for i,c,mx,tot in occ:
            print('   f%03d changed px in unit mask = %5d (%.2f%%)  maxDelta=%.0f' % (i,c,100*c/tot,mx))
