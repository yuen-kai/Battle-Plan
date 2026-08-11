import numpy as np, os
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])

ROOT = os.path.dirname(os.path.abspath(__file__))

def seg(rnd, idx, box):
    pl = plate(rnd,'numbers'); fr = load(fp(rnd,'numbers',idx))
    L = lum(fr); S = sat(fr); H = hue(fr)
    d = np.abs(fr-pl).max(-1)
    ch = d > 20
    x0,y0,x1,y1 = box
    sub = lambda a: a[y0:y1, x0:x1]
    Lc, Sc, Hc, chc, frc = sub(L), sub(S), sub(H), sub(ch), fr[y0:y1,x0:x1]
    amber = chc & (Sc>0.40) & (Hc>25) & (Hc<75) & (Lc>90)
    dark  = chc & (Lc<70)
    solid = ndimage.binary_fill_holes(amber|dark)
    # keep largest cc
    lab,n = ndimage.label(solid)
    sizes = ndimage.sum(solid, lab, range(1,n+1))
    solid = lab == (int(np.argmax(sizes))+1)
    vis = np.zeros(frc.shape, np.uint8)
    vis[...] = (frc*0.28).astype(np.uint8)
    vis[solid & dark] = (255,40,40)          # dark structural (keyline + body)
    vis[solid & amber] = (60,255,60)         # amber (rim + digits)
    return frc.astype(np.uint8), vis, solid, amber, dark, Lc, Sc, Hc

for rnd, idx, box in (('r2',14,(480,130,760,395)), ('r3',14,(490,190,750,400))):
    frc, vis, solid, amber, dark, Lc, Sc, Hc = seg(rnd, idx, box)
    s = 3
    a = Image.fromarray(frc).resize((frc.shape[1]*s, frc.shape[0]*s), Image.NEAREST)
    b = Image.fromarray(vis).resize((vis.shape[1]*s, vis.shape[0]*s), Image.NEAREST)
    out = Image.new('RGB',(a.width+b.width+16, a.height),(15,15,18))
    out.paste(a,(0,0)); out.paste(b,(a.width+16,0))
    out.save(os.path.join(ROOT,'_ad3_seg_%s.png'%rnd))
    print(rnd,'solid',solid.sum(),'amber',(solid&amber).sum(),'dark',(solid&dark).sum(), out.size)
