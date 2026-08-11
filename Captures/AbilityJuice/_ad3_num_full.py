import numpy as np, os, json
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

def cellpitch_at(pl_lum, y, halfband=45):
    """horizontal seam pitch measured in a band centred on row y"""
    H,W = pl_lum.shape
    y0,y1 = max(0,y-halfband), min(H,y+halfband)
    cm = pl_lum[y0:y1].mean(0)
    mins=[]
    for x in range(3,W-3):
        if cm[x]==cm[x-3:x+4].min() and cm[x] < cm[max(0,x-14):x+15].mean()-2.0:
            mins.append(x)
    grp=[]
    for x in mins:
        if grp and x-grp[-1][-1]<=3: grp[-1].append(x)
        else: grp.append([x])
    cen=[float(np.mean(g)) for g in grp]
    d=[cen[i+1]-cen[i] for i in range(len(cen)-1)]
    d=[v for v in d if v>120]
    return (float(np.median(d)) if d else None), cen

def badge_masks(fr, pl, ybot=0.60, thr=20):
    L,S,H = lum(fr), sat(fr), hue(fr)
    ch = np.abs(fr-pl).max(-1) > thr
    ch[int(ch.shape[0]*ybot):,:] = False
    amber = ch & (S>0.40) & (H>25) & (H<75) & (L>90)
    dark  = ch & (L<70)
    solid = ndimage.binary_fill_holes(amber|dark)
    lab,n = ndimage.label(solid)
    sizes = ndimage.sum(solid, lab, range(1,n+1))
    solid = lab == (int(np.argmax(sizes))+1)
    bright = solid & (L>100)
    lab2,n2 = ndimage.label(bright)
    digits = np.zeros_like(bright); ring = np.zeros_like(bright)
    for k in range(1,n2+1):
        c = lab2==k
        a = c.sum()
        if a < 400: continue
        ys,xs = np.where(c)
        bw,bh = xs.max()-xs.min()+1, ys.max()-ys.min()+1
        f = a/(bw*bh)
        if f < 0.30: ring |= c
        else: digits |= c
    return dict(L=L,S=S,H=H,solid=solid,digits=digits,ring=ring,amber=amber,dark=dark)

def unit_top(pl, fr, xcen, halfw=90):
    """top row of the unit silhouette (dark outline + pink body) near xcen, from the CLEAN plate."""
    L = lum(pl); S = sat(pl); Hh = hue(pl)
    # unit body = saturated pink/red, or the green health bar
    pink = (S>0.30) & (((Hh>320)|(Hh<25)))
    green = (S>0.30) & (Hh>90) & (Hh<160)
    darko = L < 60
    m = pink|green|darko
    m[:, :xcen-halfw] = False; m[:, xcen+halfw:] = False
    m[:int(pl.shape[0]*0.30),:] = False
    lab,n = ndimage.label(ndimage.binary_closing(m, np.ones((5,5))))
    sizes = ndimage.sum(m, lab, range(1,n+1))
    if n==0: return None
    big = lab == int(np.argmax(sizes))+1
    ys,xs = np.where(big)
    return int(ys.min()), int(ys.max()), int(xs.min()), int(xs.max()), int(big.sum())

if __name__=='__main__':
    res={}
    for rnd in ('r2','r3'):
        pl = plate(rnd,'numbers'); plL = lum(pl)
        # unit crown from clean plate
        u = unit_top(pl, None, 615, 110)
        print('==',rnd,'unit bbox (plate) ytop=%d ybot=%d x %d..%d px=%d'%u)
        for idx in (13,14,15,18,20,24):
            fr = load(fp(rnd,'numbers',idx))
            M = badge_masks(fr,pl)
            solid, dg, ring = M['solid'], M['digits'], M['ring']
            ys,xs = np.where(solid)
            by0,by1,bx0,bx1 = ys.min(),ys.max(),xs.min(),xs.max()
            gy,gx = np.where(dg)
            p,_ = cellpitch_at(plL, int((by0+by1)/2))
            pitch_unit,_ = cellpitch_at(plL, u[0])
            gap_px = u[0] - by1
            print('  f%03d badge %dx%d area=%d  digitInk=%d capH=%d  pitch@badge=%s pitch@unit=%s'
                  % (idx, bx1-bx0+1, by1-by0+1, solid.sum(), dg.sum(), gy.max()-gy.min()+1,
                     ('%.0f'%p) if p else '-', ('%.0f'%pitch_unit) if pitch_unit else '-'))
            if p:
                print('        capCells@badge=%.3f  footprint=%.3f cell^2  glyphBoxShare=%.1f%%  gap=%dpx=%.3f cells'
                      % ((gy.max()-gy.min()+1)/p, solid.sum()/(p*p),
                         100.0*((gx.max()-gx.min()+1)*(gy.max()-gy.min()+1))/((bx1-bx0+1)*(by1-by0+1)),
                         gap_px, gap_px/p))
