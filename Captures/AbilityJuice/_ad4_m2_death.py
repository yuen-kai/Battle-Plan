import numpy as np, os, json
from PIL import Image

R4='Captures/AbilityJuice/shots/r4/death'
def F(i): return np.asarray(Image.open('%s/f%04d.jpg'%(R4,i)).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def satv(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1)
    return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)
def hue(a):
    r,g,b=a[...,0],a[...,1],a[...,2]
    mx=a.max(axis=-1); mn=a.min(axis=-1); d=mx-mn
    h=np.zeros_like(mx)
    m=(d>1e-6)
    ir=m&(mx==r); ig=m&(mx==g)&~ir; ib=m&(mx==b)&~ir&~ig
    h[ir]=((g-b)[ir]/d[ir])%6
    h[ig]=((b-r)[ig]/d[ig])+2
    h[ib]=((r-g)[ib]/d[ib])+4
    return h*60

n=len([f for f in os.listdir(R4) if f.endswith('.jpg')])
print('frames', n, ' t=0 at frame 12 (30fps)')

# clean plate = frame 0..3 median (unit is present but static-ish); use a late frame as post plate
plate_post = np.median(np.stack([F(i) for i in range(66,72)]),axis=0)

print()
print('=== per-frame: clipped pixels, cyan area, sat/hue of coloured material ===')
print('%5s %7s  %8s %8s  %9s %9s %8s %8s' % ('f','t','clip255','L>200','cyanpx','cyan_Lmax','satmean','darkpx'))
rows=[]
for i in range(10, 32):
    a=F(i); L=lum(a); S=satv(a); H=hue(a)
    clip = ((a[...,0]>=254)&(a[...,1]>=254)&(a[...,2]>=254)).sum()
    hi = (L>200).sum()
    # cyan = hue 160-220
    cy = (H>160)&(H<220)&(S>0.45)
    cyL = L[cy].max() if cy.sum() else 0
    dark = (L<110).sum()
    # coloured effect material: differs from plate
    d = np.abs(a-plate_post).max(axis=-1)
    eff = d>28
    sm = S[eff].mean() if eff.sum() else 0
    rows.append((i,(i-12)/30.0,int(clip),int(hi),int(cy.sum()),float(cyL),float(sm),int(dark)))
    print('%5d %+7.3f  %8d %8d  %9d %9.0f %8.3f %8d' % rows[-1])

print()
print('=== builder claim: 123-142 fully clipped px from +0.167 to +0.333s (frames 17..22) ===')
for i in range(17,23):
    a=F(i)
    c1=((a[...,0]>=254)&(a[...,1]>=254)&(a[...,2]>=254)).sum()
    c2=((a[...,0]>=250)&(a[...,1]>=250)&(a[...,2]>=250)).sum()
    print('  f%02d t%+.3f  clip>=254: %d   >=250: %d' % (i,(i-12)/30.0,c1,c2))
print('baseline clipped in clean plate frames 0..5:')
for i in range(0,6):
    a=F(i); print('  f%02d  clip>=254: %d' % (i, ((a[...,0]>=254)&(a[...,1]>=254)&(a[...,2]>=254)).sum()))
