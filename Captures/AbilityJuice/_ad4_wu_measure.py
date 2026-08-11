import numpy as np, glob, os, json
from PIL import Image

def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1)
    return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)
def hue(a):
    mx=a.max(-1); mn=a.min(-1); d=mx-mn
    r,g,b=a[...,0],a[...,1],a[...,2]
    h=np.zeros_like(mx)
    m=(d>1e-6)&(mx==r); h[m]=(60*((g-b)[m]/d[m])) % 360
    m=(d>1e-6)&(mx==g); h[m]=60*((b-r)[m]/d[m])+120
    m=(d>1e-6)&(mx==b); h[m]=60*((r-g)[m]/d[m])+240
    return h

def panels(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    h,w,_ = a.shape
    pw = w//3
    return a, [a[:, i*pw:(i+1)*pw] for i in range(3)]

def stats(pan):
    L,S = lum(pan), sat(pan)
    keep = L > 8
    n = max(keep.sum(),1)
    bs = keep & (L>200) & (S>0.45)
    return dict(
        pct_bright_sat = round(100.0*bs.sum()/n, 3),
        pct_L200 = round(100.0*(keep&(L>200)).sum()/n, 2),
        Lmax = round(float(L.max()),0),
        Lp99 = round(float(np.percentile(L[keep],99)),0),
        sat_mean = round(float(S[keep].mean()),3),
        n_bs = int(bs.sum()),
        hue_bs = (round(float(np.median(hue(pan)[bs])),0) if bs.sum()>20 else None),
        clip255 = int((pan.max(-1)>=254).sum()),
        clipAll3 = int((pan.min(-1)>=254).sum()),
    )

print('=== REFERENCE (Clash Mini) per-panel pct_bright_sat  [L>200 & S>0.45] ===')
refs = sorted(glob.glob('Captures/AbilityJuice/strips/reference/*.jpg'))
allp1=[]
for p in refs:
    a, ps = panels(p)
    st = [stats(x) for x in ps]
    allp1.append(st[0]['pct_bright_sat'])
    print('%-28s p1=%6.3f%% p2=%6.3f%% p3=%6.3f%%   p1 Lp99=%3.0f satmean=%.2f' % (
        os.path.basename(p), st[0]['pct_bright_sat'], st[1]['pct_bright_sat'],
        st[2]['pct_bright_sat'], st[0]['Lp99'], st[0]['sat_mean']))
print('REF panel-1 bright&sat: min %.3f  med %.3f  max %.3f' % (min(allp1), float(np.median(allp1)), max(allp1)))

print()
for tag in ['r3/windup.jpg','r4/windup.jpg']:
    p = 'Captures/AbilityJuice/strips/'+tag
    a, ps = panels(p)
    print('=== %s  size=%s ===' % (tag, (a.shape[1], a.shape[0])))
    for i,x in enumerate(ps):
        print('  panel%d %s' % (i+1, json.dumps(stats(x))))
