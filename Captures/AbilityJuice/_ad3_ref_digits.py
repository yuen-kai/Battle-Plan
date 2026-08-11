import numpy as np, os
from PIL import Image
from scipy import ndimage
ROOT = os.path.dirname(os.path.abspath(__file__))
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(-1); mn=a.min(-1)
    return np.where(mx>1e-5,(mx-mn)/np.maximum(mx,1e-5),0.0)
def hue(a):
    mx=a.max(-1); mn=a.min(-1); d=mx-mn
    r,g,b=a[...,0],a[...,1],a[...,2]; h=np.zeros_like(mx); m=d>1e-6
    i=m&(mx==r); h[i]=((g-b)[i]/d[i])%6
    i=m&(mx==g); h[i]=((b-r)[i]/d[i])+2
    i=m&(mx==b); h[i]=((r-g)[i]/d[i])+4
    return h*60.0
def ref(name): return np.asarray(Image.open(os.path.join(ROOT,'strips','reference',name)).convert('RGB')).astype(float)

def find(name):
    im = ref(name); H,W,_ = im.shape
    L,S,Hu = lum(im), sat(im), hue(im)
    yellow = (S>0.60)&(Hu>40)&(Hu<68)&(L>150)
    dark   = L < 70
    # digits: yellow blob mostly ringed by dark
    lab,n = ndimage.label(ndimage.binary_closing(yellow, np.ones((3,3))))
    out=[]
    for k in range(1,n+1):
        c = lab==k
        a = int(c.sum())
        if a < 90 or a > 4000: continue
        ys,xs = np.where(c)
        w = int(xs.max()-xs.min()+1); h = int(ys.max()-ys.min()+1)
        if h < 12 or w < 8: continue
        if w > 3*h: continue          # skip bars
        ring = ndimage.binary_dilation(c, np.ones((7,7))) & ~ndimage.binary_dilation(c, np.ones((3,3)))
        frac = float(dark[ring].mean())
        out.append(dict(area=a,w=w,h=h,x=int(xs.min()),y=int(ys.min()),darkring=round(frac,2),
                        panel=int(xs.mean()//(W/3))))
    out = [o for o in out if o['darkring'] > 0.45]
    out.sort(key=lambda o:-o['area'])
    return out

for n in sorted(os.listdir(os.path.join(ROOT,'strips','reference'))):
    if not n.endswith('.jpg'): continue
    r = find(n)
    if r:
        print(n)
        for d in r[:8]: print('    ', d)
