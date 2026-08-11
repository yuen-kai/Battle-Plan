import numpy as np
from PIL import Image, ImageDraw

def strip_panel(p, idx=1):
    a=Image.open(p).convert('RGB')
    w,h=a.size; pw=w//3
    return a.crop((idx*pw, 0, (idx+1)*pw, h))

# r4 impact panels
d4=strip_panel('Captures/AbilityJuice/strips/r4/death.jpg')
p4=strip_panel('Captures/AbilityJuice/strips/r4/pogo.jpg')
d3=strip_panel('Captures/AbilityJuice/strips/r3/death.jpg')
p3=strip_panel('Captures/AbilityJuice/strips/r3/pogo.jpg')
print('panel size', d4.size)

def grey(im):
    a=np.asarray(im).astype(np.float32)
    L=0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
    return Image.fromarray(np.dstack([L,L,L]).astype(np.uint8))

W,H=d4.size
def row(ims, labels, title):
    s=Image.new('RGB',(W*len(ims), H+26),(10,10,10)); dr=ImageDraw.Draw(s)
    for k,(im,l) in enumerate(zip(ims,labels)):
        s.paste(im,(k*W,26)); dr.text((k*W+8,7),l,fill=(255,255,120))
    dr.text((6,7),'',fill=(255,255,255))
    return s

a=row([d4,p4],['R4 DEATH impact','R4 POGO LANDING impact'],'')
a.save('Captures/AbilityJuice/_ad4_ab_colour.jpg',quality=95)
b=row([grey(d4),grey(p4)],['R4 DEATH  (greyscale)','R4 POGO LANDING  (greyscale)'],'')
b.save('Captures/AbilityJuice/_ad4_ab_grey.jpg',quality=95)
c=row([d3,p3],['R3 DEATH impact (rejected)','R3 POGO impact (rejected)'],'')
c.save('Captures/AbilityJuice/_ad4_ab_r3.jpg',quality=95)

# squint test: downsample hard then blow back up
def squint(im, n=26):
    return im.resize((n,int(n*H/W)), Image.BOX).resize((W,H), Image.NEAREST)
d=row([squint(d4),squint(p4)],['R4 DEATH squint','R4 POGO squint'],'')
d.save('Captures/AbilityJuice/_ad4_ab_squint.jpg',quality=95)
e=row([squint(grey(d4)),squint(grey(p4))],['R4 DEATH squint GREY','R4 POGO squint GREY'],'')
e.save('Captures/AbilityJuice/_ad4_ab_squintgrey.jpg',quality=95)
print('wrote a/b sheets')

# numeric separation of the effect regions only
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def satv(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)
def hue(a):
    r,g,b=a[...,0],a[...,1],a[...,2]; mx=a.max(axis=-1); mn=a.min(axis=-1); d=mx-mn
    h=np.zeros_like(mx); m=(d>1e-6)
    ir=m&(mx==r); ig=m&(mx==g)&~ir; ib=m&(mx==b)&~ir&~ig
    h[ir]=((g-b)[ir]/d[ir])%6; h[ig]=((b-r)[ig]/d[ig])+2; h[ib]=((r-g)[ib]/d[ib])+4
    return h*60

print()
print('=== EFFECT-ONLY STATS on the impact panels (mask = differs from panel-1 board, sat>0.25) ===')
for name,pth in [('R4 death','Captures/AbilityJuice/strips/r4/death.jpg'),
                 ('R4 pogo ','Captures/AbilityJuice/strips/r4/pogo.jpg'),
                 ('R3 death','Captures/AbilityJuice/strips/r3/death.jpg'),
                 ('R3 pogo ','Captures/AbilityJuice/strips/r3/pogo.jpg')]:
    im=np.asarray(strip_panel(pth)).astype(np.float32)
    L,S,Hh=lum(im),satv(im),hue(im)
    # effect = not board floor.  board floor L~181-190 low sat
    eff=(np.abs(L-184)>35)|(S>0.30)
    eff[:60,:]=False   # badge band
    hs=Hh[eff&(S>0.35)]
    print('%s  eff_area=%6d  L_mean=%5.1f  L_p10=%5.0f  sat_mean=%.3f  hue_median=%5.0f  hue_p25=%5.0f hue_p75=%5.0f' %
          (name, eff.sum(), L[eff].mean(), np.percentile(L[eff],10), S[eff].mean(),
           np.median(hs) if len(hs) else -1, np.percentile(hs,25) if len(hs) else -1, np.percentile(hs,75) if len(hs) else -1))
