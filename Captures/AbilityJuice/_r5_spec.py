import numpy as np, sys, os
from PIL import Image
sys.path.insert(0, os.getcwd())
im = Image.open('shots/r4/shockwave/f0017.jpg').convert('RGB').resize((512,512), Image.LANCZOS)
a = np.asarray(im, dtype=np.float64)
L = 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
mx,mn = a.max(-1), a.min(-1)
S = np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0)
for thr in (200,220,235,240):
    m = L>thr
    print('L>%d : %6d px (%.3f%%)  median sat %.3f  median rgb %s'
          % (thr, m.sum(), 100*m.mean(), np.median(S[m]) if m.sum() else 0,
             np.round(np.median(a[m],axis=0),0) if m.sum() else '-'))
# where are the >235 pixels?
m = L>235
ys,xs = np.nonzero(m)
print('bbox of L>235: x %d-%d  y %d-%d' % (xs.min(),xs.max(),ys.min(),ys.max()))
vis = np.asarray(im).copy()
vis[m] = [255,0,0]
vis[(L>200)&(~m)] = [255,200,0]
Image.fromarray(vis).resize((768,768), Image.NEAREST).save('_r5_spec_f17.png')
print('wrote _r5_spec_f17.png (red = L>235, amber = L>200)')
