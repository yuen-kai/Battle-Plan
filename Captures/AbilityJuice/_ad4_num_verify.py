import numpy as np, glob
from PIL import Image
from scipy import ndimage

def lum(x): return 0.2126*x[...,0]+0.7152*x[...,1]+0.0722*x[...,2]
def sat(x):
    mx=x.max(-1); mn=x.min(-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0)
def hue(x):
    mx=x.max(-1); mn=x.min(-1); d=mx-mn
    r,g,b=x[...,0],x[...,1],x[...,2]
    h=np.zeros_like(mx)
    m=(d>1e-6)&(mx==r); h[m]=(60*((g-b)[m]/d[m]))%360
    m=(d>1e-6)&(mx==g); h[m]=60*((b-r)[m]/d[m])+120
    m=(d>1e-6)&(mx==b); h[m]=60*((r-g)[m]/d[m])+240
    return h

def digit_mask(A):
    """bright warm glyph pixels (the digits + keyline)."""
    L,S,Hh = lum(A), sat(A), hue(A)
    return (S>0.40)&(Hh>20)&(Hh<70)&(L>110)

def plate_component(A):
    m = digit_mask(A)
    d = ndimage.binary_dilation(m, np.ones((11,11)))
    lab,n = ndimage.label(d, np.ones((3,3)))
    best=None
    for k in range(1,n+1):
        c=(lab==k)&m
        if c.sum()<120: continue
        ys,xs=np.nonzero(c)
        if xs.max()-xs.min()<16: continue
        if best is None or c.sum()>best[0]: best=(c.sum(), c)
    return None if best is None else best[1]

# ---------- 1. POGO: scan every frame for clipping ----------
print('=== POGO: per-frame badge extent (1024-wide capture) ===')
fs = sorted(glob.glob('Captures/AbilityJuice/shots/r4/pogo/f*.jpg'))
fs3 = sorted(glob.glob('Captures/AbilityJuice/shots/r3/pogo/f*.jpg'))
def scan(seq, tag):
    worst = -1; worstf=None; rows=[]
    for i,f in enumerate(seq):
        A = np.asarray(Image.open(f).convert('RGB')).astype(np.float32)
        W = A.shape[1]
        # badge lives in the upper half, right of centre; exclude HP bars (green) and unit
        m = plate_component(A)
        if m is None or m.sum()<150: continue
        ys,xs = np.nonzero(m)
        if ys.mean() > W*0.6: continue
        frac = (xs.max()+1)/W
        rows.append((i, xs.min(), xs.max(), ys.min(), ys.max(), int(m.sum()), frac))
        if frac > worst: worst, worstf = frac, i
    print('%s: %d frames with a badge; worst right-edge fraction %.4f at frame %s'
          % (tag, len(rows), worst, worstf))
    for r in rows:
        flag = '   *** TOUCHES/EXCEEDS EDGE' if r[6] >= 0.998 else ''
        print('   f%03d x %4d..%4d  y %4d..%4d  area %5d  rightfrac %.4f%s' % r[:6] + '' if False else
              '   f%03d x %4d..%4d  y %4d..%4d  area %5d  rightfrac %.4f%s' % (r+(flag,)))
    return rows
r4rows = scan(fs, 'r4/pogo')
print()
r3rows = scan(fs3, 'r3/pogo')
