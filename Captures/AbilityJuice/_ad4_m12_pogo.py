import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

P='Captures/AbilityJuice/shots/r4/pogo'
def A(i): return np.asarray(Image.open('%s/f%04d.jpg'%(P,i)).convert('RGB')).astype(np.float32)
def IM(i): return Image.open('%s/f%04d.jpg'%(P,i)).convert('RGB')
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def satv(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)
def hue(a):
    r,g,b=a[...,0],a[...,1],a[...,2]; mx=a.max(axis=-1); mn=a.min(axis=-1); d=mx-mn
    h=np.zeros_like(mx); m=(d>1e-6)
    ir=m&(mx==r); ig=m&(mx==g)&~ir; ib=m&(mx==b)&~ir&~ig
    h[ir]=((g-b)[ir]/d[ir])%6; h[ig]=((b-r)[ig]/d[ig])+2; h[ib]=((r-g)[ib]/d[ib])+4
    return h*60

# impact_frame 49 at t=1.1333 -> landing t0. frame for t: 49 + round(30*(t-1.1333))
IMPACT=49
print('pogo impact frame', IMPACT)

# find effect centre
d=np.abs(A(44)-A(IMPACT)).max(axis=-1)
ys,xs=np.where(d>45); cx,cy=int(xs.mean()),int(ys.mean())
print('effect centroid',cx,cy)

# zoom sheet around landing
W=380; th=380
x0,y0=cx-W//2, cy-W//2
for tag,frames in [('A',[46,47,48,49,50]),('B',[51,53,55,58,62])]:
    s=Image.new('RGB',(th*5,th+24),(12,12,12)); dr=ImageDraw.Draw(s)
    for k,i in enumerate(frames):
        s.paste(IM(i).crop((x0,y0,x0+W,y0+W)).resize((th,th),Image.LANCZOS),(k*th,24))
        dr.text((k*th+8,6),'f%d  %+.3fs'%(i,(i-IMPACT)/30.0),fill=(255,255,90))
    s.save('Captures/AbilityJuice/_ad4_pogo_%s.jpg'%tag,quality=94)
print('wrote pogo zooms')

print()
print('=== RIDER LEGIBILITY: how much of the rider is visible each frame? ===')
# rider = the blue-ringed unit. Sample his team colour (cyan/blue ring) + his body
a=A(40); H=hue(a); S=satv(a); L=lum(a)
print('%4s %8s  %9s %9s   %s' % ('f','t','rider_px','vs_base','note'))
base=None
for i in range(40,75):
    a=A(i); H=hue(a); S=satv(a); L=lum(a)
    # rider team ring is strong blue ~205-215 deg, sat>0.5, mid luminance
    m=(H>195)&(H<225)&(S>0.45)&(L>60)&(L<200)
    reg=np.zeros((1024,1024),bool); reg[cy-220:cy+220, cx-220:cx+220]=True
    m=m&reg
    if i<=44: base = m.sum() if base is None else max(base,m.sum())
    print('%4d %+8.3f  %9d %8.0f%%   %s' % (i,(i-IMPACT)/30.0,m.sum(),100*m.sum()/max(base,1),
          'BURIED' if m.sum()<0.35*base else ''))

print()
print('=== PLUME COUNT at the aftermath frame (builder: two unequal puffs flanking the rider) ===')
post=np.median(np.stack([A(i) for i in range(94,100)]),axis=0)
for i in [55,58,60,62,64]:
    a=A(i); d=np.abs(a-post).max(axis=-1)
    m=d>34
    reg=np.zeros((1024,1024),bool); reg[cy-260:cy+260, cx-260:cx+260]=True
    m=m&reg
    m=ndimage.binary_opening(m, np.ones((5,5)))
    lab,n=ndimage.label(m)
    sizes=sorted(ndimage.sum(m,lab,range(1,n+1)),reverse=True)
    print('  f%02d %+0.3fs  blobs>=400px: %s' % (i,(i-IMPACT)/30.0,[int(s) for s in sizes if s>=400][:8]))
