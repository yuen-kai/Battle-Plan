import numpy as np
from PIL import Image

R4='Captures/AbilityJuice/shots/r4/death'
def A(i): return np.asarray(Image.open('%s/f%04d.jpg'%(R4,i)).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def satv(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1)
    return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)
def hue(a):
    r,g,b=a[...,0],a[...,1],a[...,2]
    mx=a.max(axis=-1); mn=a.min(axis=-1); d=mx-mn
    h=np.zeros_like(mx); m=(d>1e-6)
    ir=m&(mx==r); ig=m&(mx==g)&~ir; ib=m&(mx==b)&~ir&~ig
    h[ir]=((g-b)[ir]/d[ir])%6; h[ig]=((b-r)[ig]/d[ig])+2; h[ib]=((r-g)[ib]/d[ib])+4
    return h*60

# The dying unit is the PINK one (team colour). Track its pixels only, in the death cell region.
print('=== CORPSE (pink team-colour pixels) in the death cell, x 380..700, y 330..700 ===')
print('%4s %8s  %7s %7s %7s  %6s' % ('f','t','pink_px','y_top','y_bot','height'))
base=None
for i in range(6,30):
    a=A(i); H=hue(a); S=satv(a); L=lum(a)
    m=((H>320)|(H<12))&(S>0.35)&(L>40)
    reg=np.zeros_like(m); reg[330:700,380:700]=True
    m=m&reg
    ys,xs=np.where(m)
    if len(ys)<25:
        print('%4d %+8.3f  %7d %7s %7s  %6s' % (i,(i-12)/30.0,m.sum(),'-','-','-')); continue
    h=ys.max()-ys.min()
    if base is None and i<=9: base=h
    print('%4d %+8.3f  %7d %7d %7d  %6d   (%.0f%% of standing height)' % (i,(i-12)/30.0,m.sum(),ys.min(),ys.max(),h, 100*h/base if base else 0))

print()
print('=== ANY non-board material that is NOT the dark cloud, in the death cell ===')
print('   (i.e. is there a visible body at all once the cloud arrives?)')
post = np.median(np.stack([A(i) for i in range(66,73)]),axis=0)
for i in range(11,26):
    a=A(i); L=lum(a); S=satv(a); H=hue(a)
    reg=np.zeros((1024,1024),bool); reg[330:730,340:760]=True
    d=np.abs(a-post).max(axis=-1)
    eff=reg&(d>30)
    body = eff&(L>60)&(L<175)&(S<0.35)     # mid-value desaturated = corpse/dust, not void, not cyan
    cyan = eff&(H>150)&(H<230)&(S>0.35)&(L>90)
    void = eff&(L<45)
    print('  f%02d %+.3fs  void %6d   midtone %6d   cyan %6d   (total eff %6d)' % (i,(i-12)/30.0,void.sum(),body.sum(),cyan.sum(),eff.sum()))
