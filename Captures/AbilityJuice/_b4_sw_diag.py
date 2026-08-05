import numpy as np, glob, os, json
from PIL import Image, ImageFilter

D = 'Captures/AbilityJuice/shots/r3/shockwave/'
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1)
    return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)
def hue(a):
    r,g,b = a[...,0],a[...,1],a[...,2]
    mx=a.max(axis=-1); mn=a.min(axis=-1); d=mx-mn
    h=np.zeros_like(mx); m=d>1e-6
    ir=(mx==r)&m; ig=(mx==g)&m; ib=(mx==b)&m
    h[ir]=(((g-b)[ir]/d[ir])%6); h[ig]=((b-r)[ig]/d[ig])+2; h[ib]=((r-g)[ib]/d[ib])+4
    return h*60

load=lambda i: np.asarray(Image.open(D+'f%04d.jpg'%i).convert('RGB')).astype(np.float32)
plate=np.median(np.stack([load(i) for i in range(0,11)]),axis=0)
Lp=lum(plate)
print('PLATE: L mean %.1f  p5-p95 %.0f-%.0f  std %.1f' % (Lp.mean(), np.percentile(Lp,5), np.percentile(Lp,95), Lp.std()))

for f in (15,17,19):
    a=load(f); La=lum(a); Sa=sat(a); Ha=hue(a)
    m=(np.abs(a-plate).max(axis=2)>20)&(La<Lp-25)
    im=Image.fromarray((m*255).astype(np.uint8)).filter(ImageFilter.MinFilter(17))
    core=np.asarray(im)>127
    vc=La[core]
    fx=np.abs(a-plate).max(axis=2)>18
    bs = fx&(La>200)&(Sa>0.45)
    print('\nf%d  mass %d px (%.2f cells2)  core %d px' % (f, m.sum(), m.sum()/218.0**2, core.sum()))
    if core.sum():
        print('   core L: mean %.1f std %.1f  p5-p95 %.0f-%.0f (spread %.0f)'
              % (vc.mean(), vc.std(), np.percentile(vc,5), np.percentile(vc,95),
                 np.percentile(vc,95)-np.percentile(vc,5)))
    print('   plate under mass: mean %.1f  p5-p95 %.0f-%.0f' % (Lp[m].mean(), np.percentile(Lp[m],5), np.percentile(Lp[m],95)))
    print('   fx footprint %d px (%.2f cells2)   L>200 %d   L>200&S>.45 %d (%.3f%% of 1024^2)'
          % (fx.sum(), fx.sum()/218.0**2, (fx&(La>200)).sum(), bs.sum(), 100.0*bs.sum()/1024**2))
    if bs.sum():
        print('   bright-sat hue p10/p50/p90 = %s   sat med %.2f  L max %.0f'
              % ([round(float(np.percentile(Ha[bs],q))) for q in (10,50,90)], np.median(Sa[bs]), La[bs].max()))
    # spill
    hot = fx&(La>210)
    hd = np.asarray(Image.fromarray((hot*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(41)))>127
    ring = hd & (~m) & (~hot)
    if ring.sum():
        print('   spill: hot %d px, ring %d px, dL %+.1f' % (hot.sum(), ring.sum(), La[ring].mean()-Lp[ring].mean()))

# ---- radial profile of the money frame, in shot pixels
a=load(17); La=lum(a); Sa=sat(a)
m=(np.abs(a-plate).max(axis=2)>20)&(La<Lp-25)
ys,xs=np.nonzero(m); cy,cx=ys.mean(),xs.mean()
print('\ncentroid (%.0f,%.0f)' % (cx,cy))
fx=np.abs(a-plate).max(axis=2)>18
fys,fxs=np.nonzero(fx)
print('fx bbox x %d-%d (%d px)  y %d-%d (%d px)' % (fxs.min(),fxs.max(),fxs.max()-fxs.min(),fys.min(),fys.max(),fys.max()-fys.min()))

# how wide is the hot band, radially, along rays where it exists
hotm = fx&(La>200)
if hotm.any():
    hy,hx=np.nonzero(hotm)
    r=np.hypot(hx-cx,hy-cy)
    print('hot(L>200) radius px: p5 %.0f p50 %.0f p95 %.0f  -> radial extent %.0f px' %
          (np.percentile(r,5),np.percentile(r,50),np.percentile(r,95),np.percentile(r,95)-np.percentile(r,5)))
mr=np.hypot(xs-cx,ys-cy)
print('mass radius px: p5 %.0f p50 %.0f p95 %.0f max %.0f' % (np.percentile(mr,5),np.percentile(mr,50),np.percentile(mr,95),mr.max()))

# histogram of core luminance
if True:
    im=Image.fromarray((m*255).astype(np.uint8)).filter(ImageFilter.MinFilter(17))
    core=np.asarray(im)>127
    h,_=np.histogram(La[core],bins=np.arange(0,260,10))
    print('core L hist (10-wide bins from 0):', list(h))
