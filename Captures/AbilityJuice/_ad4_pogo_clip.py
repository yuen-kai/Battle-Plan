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

for rnd in ('r3','r4'):
    fs = sorted(glob.glob(f'Captures/AbilityJuice/shots/{rnd}/pogo/f*.jpg'))
    print(f'--- {rnd}/pogo: {len(fs)} frames, size {Image.open(fs[0]).size}')
    hits = []
    for i in range(40, min(80, len(fs))):
        A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
        W = A.shape[1]
        L,S,Hh = lum(A), sat(A), hue(A)
        # gold digit/keyline: strongly saturated warm, bright
        m = (S>0.55)&(Hh>35)&(Hh<62)&(L>130)
        m[520:, :] = False           # ignore lower half (HP bars / board)
        lab,n = ndimage.label(ndimage.binary_dilation(m, np.ones((13,13))), np.ones((3,3)))
        for k in range(1, n+1):
            c = (lab==k)&m
            if c.sum() < 250: continue
            ys,xs = np.nonzero(c)
            w = xs.max()-xs.min()+1; h = ys.max()-ys.min()+1
            if w < 25 or h < 20: continue
            hits.append((i, xs.min(), xs.max(), ys.min(), ys.max(), int(c.sum()), (xs.max()+1)/W))
    for h in hits:
        edge = '  *** AT FRAME EDGE' if h[2] >= 1020 or h[1] <= 3 else ''
        print('   f%03d  x %4d..%4d (w=%3d)  y %4d..%4d (h=%3d)  area %5d  right %.4f%s' % (
            h[0], h[1], h[2], h[2]-h[1]+1, h[3], h[4], h[4]-h[3]+1, h[5], h[6], edge))
    if hits:
        print('   >>> max right fraction over the badge lifetime: %.4f' % max(x[6] for x in hits))
