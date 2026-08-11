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

CELL = 158.0
fs = sorted(glob.glob('Captures/AbilityJuice/shots/r4/death/f*.jpg'))
print('frame   t      badge cx,cy    burst cx,cy    offset(px)  offset(cells)  badge rise since spawn')
first=None
for i in range(12, 34):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    g = (S>0.40)&(Hh>10)&(Hh<50)&(L>120); g[600:,:]=False; g[:,:300]=False
    lab,n = ndimage.label(ndimage.binary_dilation(g, np.ones((15,15))), np.ones((3,3)))
    if n==0: continue
    sizes=[((lab==k)&g).sum() for k in range(1,n+1)]
    if max(sizes) < 400: continue
    b = (lab==int(np.argmax(sizes))+1)&g
    by,bx = np.nonzero(b)
    # death burst: very dark opaque blob mid-frame
    dk = (L<70); dk[:250,:]=False; dk[750:,:]=False
    lab2,n2 = ndimage.label(dk, np.ones((3,3)))
    if n2==0: continue
    s2=[ (lab2==k).sum() for k in range(1,n2+1)]
    d2 = lab2==int(np.argmax(s2))+1
    dy,dx = np.nonzero(d2)
    off = np.hypot(bx.mean()-dx.mean(), by.mean()-dy.mean())
    if first is None: first = by.mean()
    print('%5d %+6.3f  %6.0f,%-6.0f %6.0f,%-6.0f  %8.0f      %.2f          %+.0f px' % (
        i, i/30.0-0.4, bx.mean(), by.mean(), dx.mean(), dy.mean(), off, off/CELL, by.mean()-first))
