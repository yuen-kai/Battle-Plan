import numpy as np, os, json
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

def analyse(rnd, shot, idx, pl=None, ybot=0.60, thr=20):
    if pl is None: pl = plate(rnd, shot)
    fr = load(fp(rnd, shot, idx))
    L, S, H = lum(fr), sat(fr), hue(fr)
    ch = np.abs(fr-pl).max(-1) > thr
    ch[int(ch.shape[0]*ybot):,:] = False
    amber = ch & (S>0.40) & (H>25) & (H<75) & (L>90)
    dark  = ch & (L<70)
    solid = ndimage.binary_fill_holes(amber|dark)
    lab,n = ndimage.label(solid)
    if n==0: return None
    sizes = ndimage.sum(solid, lab, range(1,n+1))
    solid = lab == (int(np.argmax(sizes))+1)
    # bright ink inside the plate
    bright = solid & (L>100)
    lab2,n2 = ndimage.label(bright)
    comps = []
    for k in range(1,n2+1):
        m = lab2==k
        a = m.sum()
        if a < 200: continue
        ys,xs = np.where(m)
        bw, bh = xs.max()-xs.min()+1, ys.max()-ys.min()+1
        comps.append(dict(k=k, area=int(a), x0=int(xs.min()), x1=int(xs.max()),
                          y0=int(ys.min()), y1=int(ys.max()), w=int(bw), h=int(bh),
                          fill=float(a/(bw*bh))))
    comps.sort(key=lambda c:-c['area'])
    return dict(fr=fr, L=L, S=S, H=H, solid=solid, bright=bright, lab2=lab2, comps=comps)

if __name__=='__main__':
    for rnd in ('r2','r3'):
        pl = plate(rnd,'numbers')
        print('=====',rnd)
        for idx in (13,14,15,16,18,20,24,28):
            A = analyse(rnd,'numbers',idx,pl)
            ys,xs = np.where(A['solid'])
            print(' f%03d solid=%6d bbox=%dx%d @(%d,%d)' % (idx, A['solid'].sum(),
                  xs.max()-xs.min()+1, ys.max()-ys.min()+1, xs.min(), ys.min()))
            for c in A['comps'][:4]:
                print('     comp a=%6d bbox=%3dx%3d @(%d,%d) fill=%.2f' % (c['area'],c['w'],c['h'],c['x0'],c['y0'],c['fill']))
