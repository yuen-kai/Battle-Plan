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

print('=== "80" badge over its life (r4/numbers) ===')
print('frame    t      w    h    area  cy   opacity-proxy(medL)  scale-vs-peak')
fs = sorted(glob.glob('Captures/AbilityJuice/shots/r4/numbers/f*.jpg'))
rows=[]
for i in range(8, 40):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    m = (S>0.45)&(Hh>35)&(Hh<62)&(L>110); m[600:,:]=False
    if m.sum()<200: continue
    lab,n = ndimage.label(ndimage.binary_dilation(m, np.ones((15,15))), np.ones((3,3)))
    sizes=[((lab==k)&m).sum() for k in range(1,n+1)]
    b = (lab==int(np.argmax(sizes))+1)&m
    if b.sum()<200: continue
    ys,xs = np.nonzero(b)
    rows.append((i, i/30.0-0.4, xs.max()-xs.min()+1, ys.max()-ys.min()+1, int(b.sum()), ys.mean(), float(np.median(L[b]))))
peak = max(r[2] for r in rows)
for r in rows:
    print('%5d %+6.3f %4d %4d %6d %5.0f  %6.0f   %.3f' % (r[0],r[1],r[2],r[3],r[4],r[5],r[6], r[2]/peak))

# --- find reference damage numbers automatically and crop them
print('\n=== locating Clash Mini damage numbers ===')
import os
for f in ['cm-8newabilities-07.jpg','cm-everyability-22.jpg','cm-8newabilities-13.jpg','cm-8newabilities-20.jpg']:
    im = Image.open('Captures/AbilityJuice/strips/reference/'+f).convert('RGB')
    A = np.asarray(im).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    # small saturated glyph clusters that are NOT the big effects: bounded size, high sat
    m = (S>0.5)&(L>120)
    lab,n = ndimage.label(m, np.ones((3,3)))
    obj = ndimage.find_objects(lab)
    cands=[]
    for k,sl in enumerate(obj):
        if sl is None: continue
        h = sl[0].stop-sl[0].start; w = sl[1].stop-sl[1].start
        a = (lab[sl]==k+1).sum()
        if 12<=h<=42 and 8<=w<=60 and a>=60:
            cands.append((a, sl))
    cands.sort(reverse=True, key=lambda c:c[0])
    print(f, 'candidates:', [(c[0], c[1][1].start, c[1][0].start, c[1][1].stop-c[1][1].start, c[1][0].stop-c[1][0].start) for c in cands[:6]])
