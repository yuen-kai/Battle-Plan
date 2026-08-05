import numpy as np
from PIL import Image, ImageDraw

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

a9=A(9); H=hue(a9); S=satv(a9); L=lum(a9)
m=(H>320)&(H<360)&(S>0.35)&(L>40)
ys,xs=np.where(m)
print('f9 pink pixels bbox x %d..%d  y %d..%d  n=%d' % (xs.min(),xs.max(),ys.min(),ys.max(),m.sum()))
print('mean RGB of pink', a9[m].mean(axis=0), 'hue', H[m].mean(), 'sat', S[m].mean())

# locate the badge in f16
a16=A(16); H16=hue(a16); S16=satv(a16); L16=lum(a16)
badge=(H16>15)&(H16<55)&(S16>0.5)&(L16>90)
ys,xs=np.where(badge)
print('f16 badge-ish (orange) bbox x %d..%d  y %d..%d n=%d'%(xs.min(),xs.max(),ys.min(),ys.max(),badge.sum()))

# visualise masks on f16
for i in [12,14,16,18]:
    a=A(i); H=hue(a); S=satv(a); L=lum(a)
    vis=(a*0.30).astype(np.uint8)
    pink=(H>320)&(H<360)&(S>0.35)&(L>40)
    cyan=(H>150)&(H<230)&(S>0.35)&(L>90)
    void=(L<45)
    vis[void]=[255,0,255]
    vis[cyan]=[0,255,255]
    vis[pink]=[255,255,0]
    Image.fromarray(vis).crop((330,290,790,760)).resize((420,430)).save('Captures/AbilityJuice/_ad4_seg_%d.png'%i)
seg=Image.new('RGB',(420*4,430+22),(10,10,10))
d=ImageDraw.Draw(seg)
for k,i in enumerate([12,14,16,18]):
    seg.paste(Image.open('Captures/AbilityJuice/_ad4_seg_%d.png'%i),(k*420,22))
    d.text((k*420+8,6),'f%d %+0.3fs  yellow=team pink, cyan=cyan, magenta=L<45'%(i,(i-12)/30.0),fill=(255,255,255))
seg.save('Captures/AbilityJuice/_ad4_seg.png')
print('wrote seg')
