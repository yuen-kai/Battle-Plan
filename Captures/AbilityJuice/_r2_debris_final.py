import numpy as np
from PIL import Image
import os
from scipy import ndimage as ndi

ROOT=os.path.dirname(os.path.abspath(__file__))
CELL=199.0; PXWU=CELL/2.7; FPS=30.0; T0=12
R2=os.path.join(ROOT,'shots/r2/debris')
def load(p): return np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(-1);mn=a.min(-1);return np.where(mx>1e-5,(mx-mn)/np.maximum(mx,1e-5),0.)

print("="*90)
print("A) SQUINT COVERAGE — fraction of the impact panel occupied by 'event' at 1/9 scale")
def squint_cov(path, panel):
    im=Image.open(os.path.join(ROOT,path)).convert('RGB')
    p=im.crop((panel*520,0,panel*520+512,512)).resize((57,57), Image.LANCZOS)
    a=np.asarray(p).astype(np.float32); L=lum(a); S=sat(a)
    # 'event' = pixel far from that panel's own modal background in luminance or saturation
    bgL=np.median(L); bgS=np.median(S)
    ev=(np.abs(L-bgL)>28)|(np.abs(S-bgS)>0.18)
    return 100*ev.mean(), bgL, float(np.percentile(L,99)-np.percentile(L,1))
for nm,p in [('OURS r2','strips/r2/debris.jpg'),
             ('OURS r1','strips/r1/debris.jpg'),
             ('CM clashabilities-04','strips/reference/cm-clashabilities-04.jpg'),
             ('CM 8newabilities-13','strips/reference/cm-8newabilities-13.jpg'),
             ('CM everyability-05','strips/reference/cm-everyability-05.jpg'),
             ('CM everyability-23','strips/reference/cm-everyability-23.jpg')]:
    c1,_,_=squint_cov(p,0); c2,bg,rng=squint_cov(p,1); c3,_,_=squint_cov(p,2)
    print("  %-22s  panel coverage  windup %4.1f%%  IMPACT %4.1f%%  aftermath %4.1f%%   (panel2 bg lum %.0f, range %.0f)"%(nm,c1,c2,c3,bg,rng))

print()
print("="*90)
print("B) FLASH vs MASS — do they ever coincide?")
fr=sorted(f for f in os.listdir(R2) if f.endswith('.jpg'))
pre=np.stack([load(os.path.join(R2,f)) for f in fr[:6]]); post=np.stack([load(os.path.join(R2,f)) for f in fr[-6:]])
plate=np.median(np.concatenate([pre,post]),axis=0); pl=lum(plate)
print("   t      clipped(255)px   dark-material(cell^2)   product")
best=None
for i in range(12,32):
    im=load(os.path.join(R2,fr[i])); L=lum(im)
    m=np.abs(im-plate).max(-1)>12
    clip=int(((im>=250).all(-1)&m).sum())
    dark=float((m&(pl-L>60)).sum())/CELL**2
    print("  %+0.3f   %6d           %.3f                %.1f"%((i-T0)/FPS, clip, dark, clip*dark))
print()
print("="*90)
print("C) LIFT — track the single highest hero chunk against the ground")
# ground plane at the burst origin: take the effect's lowest extent at t=0 as ground datum
im0=load(os.path.join(R2,fr[12])); m0=np.abs(im0-plate).max(-1)>12
gy=np.nonzero(m0)[0].max()
print("   ground datum y (effect base at contact) = %d"%gy)
print("   t      top_y   lift_px   lift/cell   lift/wu(horiz-scale)")
for i in range(12,26):
    im=load(os.path.join(R2,fr[i])); L=lum(im)
    m=np.abs(im-plate).max(-1)>12
    warm=(im[...,0]-im[...,2])>10
    ch=m&(L<140)&warm
    lab,n=ndi.label(ch)
    if n==0: continue
    sz=ndi.sum(ch,lab,range(1,n+1))
    keep=[k+1 for k,s in enumerate(sz) if s>=150]
    if not keep: continue
    ys,_=np.nonzero(np.isin(lab,keep))
    top=ys.min()
    print("  %+0.3f   %4d    %4d      %.2f        %.1f"%((i-T0)/FPS, top, gy-top, (gy-top)/CELL, (gy-top)/PXWU))

print()
print("="*90)
print("D) AFTERMATH PANEL (t=+0.433) — what is actually on screen")
im=load(os.path.join(R2,fr[25])); L=lum(im)
m=np.abs(im-plate).max(-1)>12
lab,n=ndi.label(m)
sz=ndi.sum(m,lab,range(1,n+1)); objs=ndi.find_objects(lab)
blobs=[]
for k,s in enumerate(sz):
    if s<150: continue
    sl=objs[k]; w=(sl[1].stop-sl[1].start)/CELL; h=(sl[0].stop-sl[0].start)/CELL
    blobs.append((max(w,h), s/CELL**2, w,h))
blobs.sort(reverse=True)
print("   %d blobs >=150px.  sizes(max-dim cells): %s"%(len(blobs), ", ".join("%.2f"%b[0] for b in blobs)))
if blobs:
    dd=[b[0] for b in blobs]
    print("   largest %.2f  median %.2f  ratio %.1f:1   total covered %.2f cell^2"%(dd[0],np.median(dd),dd[0]/np.median(dd),sum(b[1] for b in blobs)))
print("   effect lum p50 = %.1f vs floor 186   -> delta %.0f"%(np.percentile(L[m],50), np.percentile(L[m],50)-186))
