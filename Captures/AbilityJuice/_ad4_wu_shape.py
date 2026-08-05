import numpy as np, glob
from PIL import Image

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

# ---- cell pitch from the clean board (frame 0), via vertical tile seams in a clear band
A0 = np.asarray(Image.open(fs[0]).convert('RGB')).astype(np.float32)
L0 = lum(A0)
band = L0[600:640, :].mean(0)
d = np.abs(np.diff(band))
peaks = [i for i in range(2, len(d)-2) if d[i] > 3 and d[i] == max(d[i-2:i+3])]
print('seam x positions in y=600..640 band:', peaks)
if len(peaks) > 2:
    gaps = np.diff(peaks)
    gaps = [g for g in gaps if g > 40]
    print('seam gaps:', gaps, ' median cell pitch = %.0f px' % np.median(gaps))

def charge_mask(A):
    L,S,Hh = lum(A), sat(A), hue(A)
    m = (L>200)&(S>0.45)&(Hh>25)&(Hh<75)
    m[:, int(A.shape[1]*0.5):] = False
    return m

def unit_mask(A):
    """caster body: dark outline + pink head + brown gun, left region, excluding board."""
    L,S,Hh = lum(A), sat(A), hue(A)
    pink = (S>0.35)&((Hh>320)|(Hh<8))&(L>60)&(L<210)
    m = np.zeros(L.shape, bool); m[:, :512] = pink[:, :512]
    return m

print('\nframe   chargeArea  IoU(prev)  bbox(w,h)   centroid-drift-px')
prevm = None; prevc = None
for i in range(18, 46):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    m = charge_mask(A)
    ys,xs = np.nonzero(m)
    c = (xs.mean(), ys.mean())
    if prevm is not None:
        iou = (m & prevm).sum() / max((m | prevm).sum(),1)
        dr = np.hypot(c[0]-prevc[0], c[1]-prevc[1])
    else:
        iou, dr = float('nan'), float('nan')
    print('%5d %10d   %6.3f    %4dx%-4d   %6.2f%s' % (
        i, m.sum(), iou, xs.max()-xs.min()+1, ys.max()-ys.min()+1, dr,
        '  <== PANEL' if i in (22,40) else ''))
    prevm, prevc = m, c

# shape-normalised IoU between panel1 and panel2 charge (translation-aligned)
A22 = np.asarray(Image.open(fs[22]).convert('RGB')).astype(np.float32)
A40 = np.asarray(Image.open(fs[40]).convert('RGB')).astype(np.float32)
m22, m40 = charge_mask(A22), charge_mask(A40)
def crop(m):
    ys,xs=np.nonzero(m); return m[ys.min():ys.max()+1, xs.min():xs.max()+1]
c22, c40 = crop(m22), crop(m40)
im22 = Image.fromarray((c22*255).astype(np.uint8)).resize(c40.shape[::-1], Image.NEAREST)
r22 = np.asarray(im22) > 127
print('\npanel1 vs panel2 charge, scale+translation aligned IoU = %.3f' % (
    (r22 & c40).sum() / max((r22 | c40).sum(),1)))

# unit body extent for the "does it swallow the unit" question
for i in (0, 22, 40):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    um = unit_mask(A)
    ys,xs = np.nonzero(um)
    sel = xs < xs.min()+95
    print('f%02d caster pink-head bbox = %dx%d px, area %d' % (
        i, xs[sel].max()-xs[sel].min()+1, ys[sel].max()-ys[sel].min()+1, sel.sum()))

# interior lighting: luminance std inside the charge, eroded
from scipy import ndimage
for i in (22, 40):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    m = charge_mask(A)
    er = ndimage.binary_erosion(m, np.ones((7,7)))
    L = lum(A)
    print('f%02d charge eroded-core: N=%d  L mean %.1f  std %.2f  p5 %.0f p95 %.0f' % (
        i, er.sum(), L[er].mean(), L[er].std(), np.percentile(L[er],5), np.percentile(L[er],95)))
