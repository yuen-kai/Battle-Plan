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

# tight region on the victim only: x 400..700, y 380..700 (excludes badge at y<350 and shooter at x<200)
Y0,Y1,X0,X1 = 380,720,400,700
print('=== VICTIM team-colour silhouette, region x%d..%d y%d..%d (badge excluded) ==='%(X0,X1,Y0,Y1))
print('%4s %8s  %8s %6s %6s %7s' % ('f','t','pink_px','ytop','ybot','height'))
base=None
for i in range(6,30):
    a=A(i); H=hue(a); S=satv(a); L=lum(a)
    m=(H>320)&(H<360)&(S>0.35)&(L>40)
    sub=m[Y0:Y1,X0:X1]
    ys,xs=np.where(sub)
    if len(ys)<20:
        print('%4d %+8.3f  %8d %6s %6s %7s  <-- team colour GONE' % (i,(i-12)/30.0,sub.sum(),'-','-','-')); continue
    h=ys.max()-ys.min()
    if i==9: base=h
    print('%4d %+8.3f  %8d %6d %6d %7d   %s' % (i,(i-12)/30.0,sub.sum(),ys.min()+Y0,ys.max()+Y0,h,
          ('%.0f%% of standing'%(100*h/base)) if base else ''))

print()
print('=== FULL victim silhouette (anything not board) in same tight region ===')
post = np.median(np.stack([A(i) for i in range(66,73)]),axis=0)
print('%4s %8s  %8s %6s %6s %7s  %s' % ('f','t','area','ytop','ybot','height','note'))
b2=None
for i in range(6,30):
    a=A(i); d=np.abs(a-post).max(axis=-1)
    sub=(d>30)[Y0:Y1,X0:X1]
    ys,xs=np.where(sub)
    if len(ys)<20: print('%4d %+8.3f  none'%(i,(i-12)/30.0)); continue
    h=ys.max()-ys.min()
    if i==9: b2=h
    print('%4d %+8.3f  %8d %6d %6d %7d  %s' % (i,(i-12)/30.0,sub.sum(),ys.min()+Y0,ys.max()+Y0,h,
          ('%.0f%% of standing'%(100*h/b2)) if b2 else ''))

# hard zoom on f11..f15 around the victim
th=430
s=Image.new('RGB',(th*5,th+24),(12,12,12)); dr=ImageDraw.Draw(s)
for k,i in enumerate([11,12,13,14,15]):
    c=Image.open('%s/f%04d.jpg'%(R4,i)).convert('RGB').crop((420,390,700,670)).resize((th,th),Image.LANCZOS)
    s.paste(c,(k*th,24)); dr.text((k*th+8,6),'f%d  %+.3fs'%(i,(i-12)/30.0),fill=(255,255,90))
s.save('Captures/AbilityJuice/_ad4_corpse_zoom.jpg',quality=95)
print('wrote corpse zoom')
