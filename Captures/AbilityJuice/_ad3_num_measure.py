import numpy as np, os, json, colorsys
from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))
def load(p): return np.asarray(Image.open(p).convert('RGB')).astype(np.float64)
def lum(a): return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def sat(a):
    mx = a.max(-1); mn = a.min(-1)
    return np.where(mx > 1e-5, (mx-mn)/np.maximum(mx,1e-5), 0.0)
def hue(a):
    mx = a.max(-1); mn = a.min(-1); d = mx-mn
    r,g,b = a[...,0],a[...,1],a[...,2]
    h = np.zeros_like(mx)
    m = (d>1e-6)
    idx = m & (mx==r); h[idx] = ((g-b)[idx]/d[idx]) % 6
    idx = m & (mx==g); h[idx] = ((b-r)[idx]/d[idx]) + 2
    idx = m & (mx==b); h[idx] = ((r-g)[idx]/d[idx]) + 4
    return h*60.0

def fp(rnd, shot, i): return os.path.join(ROOT,'shots',rnd,shot,'f%04d.jpg'%i)
def frames(rnd, shot):
    d = os.path.join(ROOT,'shots',rnd,shot)
    return sorted(f for f in os.listdir(d) if f.endswith('.jpg'))
def plate(rnd, shot, n=6):
    fs = frames(rnd, shot)
    d = os.path.join(ROOT,'shots',rnd,shot)
    pre = np.stack([load(os.path.join(d,f)) for f in fs[:n]])
    post = np.stack([load(os.path.join(d,f)) for f in fs[-n:]])
    return np.median(np.concatenate([pre,post]),axis=0)

def largest_cc(mask):
    from scipy import ndimage
    lab, n = ndimage.label(mask)
    if n == 0: return mask
    sizes = ndimage.sum(mask, lab, range(1, n+1))
    k = int(np.argmax(sizes)) + 1
    return lab == k

def fill(mask):
    from scipy import ndimage
    return ndimage.binary_fill_holes(mask)

def badge_parts(fr, pl, thr=20, ybot=0.60):
    """Return dict of masks: solid (badge body incl rim + keyline), rim, body, digit."""
    d = np.abs(fr-pl).max(-1)
    ch = d > thr
    ch[int(ch.shape[0]*ybot):,:] = False
    L = lum(fr); S = sat(fr); H = hue(fr)
    # amber = hue 35..60, sat>0.45
    amber = ch & (S>0.42) & (H>28) & (H<70)
    # keyline / dark body: very dark
    dark = ch & (L<70)
    seed = fill(largest_cc(amber | dark))
    solid = seed
    # inside solid, classify
    Ls = L*solid
    digit = solid & (L>110) & (S>0.20) & (H>28) & (H<75)
    rim   = solid & (L>110) & (S>0.42) & (H>28) & (H<70) & (~digit)
    return dict(solid=solid, amber=amber, dark=dark, digit=digit, L=L, S=S, H=H)

if __name__ == '__main__':
    from scipy import ndimage
    out = {}
    for rnd, idx in (('r2',14), ('r3',14)):
        pl = plate(rnd,'numbers')
        fr = load(fp(rnd,'numbers',idx))
        P = badge_parts(fr, pl)
        solid = P['solid']
        ys,xs = np.where(solid)
        print('==',rnd,'f%03d'%idx,'solid px',solid.sum(),'bbox x',xs.min(),xs.max(),'y',ys.min(),ys.max(),
              'w',xs.max()-xs.min()+1,'h',ys.max()-ys.min()+1)
        # digit connected components
        dg = P['digit'] & solid
        lab,n = ndimage.label(dg)
        sizes = ndimage.sum(dg, lab, range(1,n+1))
        order = np.argsort(sizes)[::-1]
        print('   digit comps', n, 'top sizes', [int(sizes[o]) for o in order[:6]])
        keep = np.zeros_like(dg)
        for o in order[:2]:
            keep |= (lab == o+1)
        gy,gx = np.where(keep)
        print('   digit ink px', keep.sum(), 'bbox x',gx.min(),gx.max(),'y',gy.min(),gy.max(),
              'w',gx.max()-gx.min()+1,'h',gy.max()-gy.min()+1)
        np.save(os.path.join(ROOT,'_ad3_%s_solid.npy'%rnd), solid)
        np.save(os.path.join(ROOT,'_ad3_%s_digit.npy'%rnd), keep)
