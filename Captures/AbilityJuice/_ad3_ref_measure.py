import numpy as np, os, json
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
def ref(name):
    return np.asarray(Image.open(os.path.join(ROOT,'strips','reference',name)).convert('RGB')).astype(float)

# --- cell pitch on the CM board: autocorrelation of the checkerboard along x
def pitch(name, panel=2, yband=(0.55,0.85)):
    im = ref(name); H,W,_ = im.shape
    pw = W//3
    x0 = panel*pw + int(pw*0.12); x1 = panel*pw + int(pw*0.88)
    Lb = lum(im[int(H*yband[0]):int(H*yband[1]), x0:x1])
    prof = Lb.mean(0); prof = prof - prof.mean()
    ac = np.correlate(prof, prof, 'full')[len(prof)-1:]
    ac /= ac[0]
    # first strong peak after the first zero crossing
    z = np.argmax(ac < 0)
    seg = ac[z:]
    k = int(np.argmax(seg)) + z
    return k, ac[k], (x1-x0)

for n in ('cm-everyability-20.jpg','cm-everyability-22.jpg','cm-everyability-03.jpg','cm-everyability-27.jpg'):
    print(n, 'pitch', pitch(n))
