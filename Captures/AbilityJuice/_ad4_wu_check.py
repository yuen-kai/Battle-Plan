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

fs = sorted(glob.glob('Captures/AbilityJuice/shots/r4/windup/f*.jpg'))

# --- 1. is the CAMERA static? seams in a board band far from the action
def seams(A, y0=880, y1=920):
    band = lum(A)[y0:y1, :].mean(0)
    d = np.abs(np.diff(band))
    return [i for i in range(2,len(d)-2) if d[i]>3 and d[i]==max(d[i-2:i+3])]
for i in (0, 22, 40, 45, 54):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    print('f%02d bottom-band seams:' % i, seams(A)[:8])

# --- 2. interior variation over the WHOLE opaque yellow body (no luminance gate)
print()
for i in (18, 22, 30, 40, 45):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    body = (S>0.45)&(Hh>25)&(Hh<75)&(L>140)
    body[:, 512:] = False
    lab,n = ndimage.label(body)
    if n:
        sizes = ndimage.sum(body, lab, range(1,n+1))
        body = lab == (np.argmax(sizes)+1)
    er = ndimage.binary_erosion(body, np.ones((7,7)))
    if er.sum() < 50: print('f%02d too small'%i); continue
    Ls = L[er]
    # local 9x9 contrast, the round-3 metric
    mn = ndimage.uniform_filter(L, 9); sq = ndimage.uniform_filter(L*L, 9)
    loc = np.sqrt(np.maximum(sq-mn*mn, 0))
    print('f%02d yellow BODY core: N=%5d  L mean %.1f  std %5.2f  p5 %3.0f p95 %3.0f  local9x9 %.2f  satmean %.2f' % (
        i, er.sum(), Ls.mean(), Ls.std(), np.percentile(Ls,5), np.percentile(Ls,95),
        loc[er].mean(), S[er].mean()))

# --- 3. the shards: how many, how big, what colour
print()
for i in (22, 40):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    # dark reddish-brown shards
    sh = (L<120)&(S>0.35)&(((Hh>0)&(Hh<40))|(Hh>340))
    sh[:, 512:] = False
    lab,n = ndimage.label(sh)
    sizes = ndimage.sum(sh, lab, range(1,n+1)) if n else []
    big = [s for s in sizes if s>=40]
    print('f%02d shard blobs >=40px: %d  sizes %s  medL %.0f medS %.2f medHue %.0f' % (
        i, len(big), sorted([int(s) for s in big], reverse=True)[:10],
        np.median(L[sh]) if sh.sum() else 0, np.median(S[sh]) if sh.sum() else 0,
        np.median(Hh[sh]) if sh.sum() else 0))

# --- 4. does the charge occlude the caster's health bar?
print()
for i in (12, 22, 40, 45):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    g = (Hh>80)&(Hh<160)&(S>0.45)&(L>70); g[:,512:]=False
    ys,xs = np.nonzero(g)
    print('f%02d caster HP bar: area %4d  x %3d..%3d  (width %3d px)' % (
        i, g.sum(), xs.min(), xs.max(), xs.max()-xs.min()+1))
