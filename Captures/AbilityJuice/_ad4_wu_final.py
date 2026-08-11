import numpy as np, glob
from PIL import Image, ImageFilter
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

fs = sorted(glob.glob('Captures/AbilityJuice/shots/r4/windup/f*.jpg'))

# caster HP bar: connected green component closest to the caster
for i in (12, 22, 40, 45):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    g = (Hh>80)&(Hh<160)&(S>0.45)&(L>70); g[:,512:]=False
    lab,n = ndimage.label(g, np.ones((3,3)))
    best=None
    for k in range(1,n+1):
        ys,xs = np.nonzero(lab==k)
        if len(xs) < 80: continue
        if best is None or len(xs) > best[0]: best = (len(xs), xs.min(), xs.max(), ys.mean())
    print('f%02d caster HP bar component: area %4d  x %3d..%3d  width %3d px' % (i,)+ ()  if False else
          'f%02d caster HP bar component: area %4d  x %3d..%3d  width %3d px' % (i, best[0], best[1], best[2], best[2]-best[1]+1))

# shards inside a box around the charge
print()
for i in (22, 40):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    ch = (L>200)&(S>0.45)&(Hh>25)&(Hh<75); ch[:,512:]=False
    ys,xs = np.nonzero(ch)
    x0,x1,y0,y1 = xs.min()-70, xs.max()+70, ys.min()-70, ys.max()+70
    box = np.zeros(L.shape, bool); box[max(y0,0):y1, max(x0,0):x1] = True
    sh = box & (L<130) & (S>0.30) & (((Hh<45)|(Hh>335)))
    lab,n = ndimage.label(sh, np.ones((3,3)))
    blobs=[]
    for k in range(1,n+1):
        m = lab==k
        if m.sum()<60: continue
        yy,xx = np.nonzero(m)
        blobs.append((int(m.sum()), xx.max()-xx.min()+1, yy.max()-yy.min()+1, float(np.median(L[m]))))
    blobs.sort(reverse=True)
    print('f%02d shard-like dark blobs near charge: %d' % (i, len(blobs)))
    for b in blobs[:8]: print('      area %5d  %3dx%-3d  medL %.0f' % b)

# ---- squint test: 1/8 scale then blur, ours vs reference
def squint(p, out):
    im = Image.open(p).convert('RGB')
    w,h = im.size
    sm = im.resize((w//8, h//8), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.8))
    sm.resize((w//2, h//2), Image.NEAREST).save(out)

squint('Captures/AbilityJuice/strips/r4/windup.jpg', 'Captures/AbilityJuice/_ad4_wu_squint.png')
squint('Captures/AbilityJuice/strips/reference/cm-everyability-22.jpg', 'Captures/AbilityJuice/_ad4_ref22_squint.png')
squint('Captures/AbilityJuice/strips/reference/cm-8newabilities-18.jpg', 'Captures/AbilityJuice/_ad4_ref18_squint.png')
print('\nsquints written')
