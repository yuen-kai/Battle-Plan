import numpy as np
from PIL import Image, ImageDraw
import os
from scipy import ndimage as ndi

ROOT=os.path.dirname(os.path.abspath(__file__))
R2=os.path.join(ROOT,'shots/r2/debris')
CELL=199.0; WU=2.7; PXWU=CELL/WU; FPS=30.0; T0=12
def load(p): return np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]

fr=sorted(f for f in os.listdir(R2) if f.endswith('.jpg'))
pre=np.stack([load(os.path.join(R2,f)) for f in fr[:6]])
post=np.stack([load(os.path.join(R2,f)) for f in fr[-6:]])
plate=np.median(np.concatenate([pre,post]),axis=0); pl=lum(plate)

print("### DUST PUFF footprint & opacity")
print(" seam-occlusion test: clean floor seam contrast ~39; opaque material must drop it below 8")
# locate a vertical seam that passes under the effect. seams at x=412,611
for fi in [12,14,16,19,22,25,28]:
    im=load(os.path.join(R2,'f%04d.jpg'%fi)); L=lum(im)
    d=np.abs(im-plate).max(-1); m=d>12
    warm=(im[...,0]-im[...,2])>10
    dust = m&(~warm)&(L<170)
    lab,n=ndi.label(dust)
    if n:
        sz=ndi.sum(dust,lab,range(1,n+1)); k=int(np.argmax(sz))+1
        ys,xs=np.nonzero(lab==k)
        w=(xs.max()-xs.min()+1)/CELL; h=(ys.max()-ys.min()+1)/CELL
        fill = sz[k-1]/((xs.max()-xs.min()+1)*(ys.max()-ys.min()+1))
        print(" t=%+0.3f  largest dust blob %.2f x %.2f cells  area %.2f c2  bbox-fill %.0f%%  total dust %.2f c2"%(
            (fi-T0)/FPS, w,h, sz[k-1]/CELL**2, 100*fill, dust.sum()/CELL**2))
    # seam contrast under effect
    for sx in (412,611):
        col = slice(sx-9,sx+10)
        # rows where effect covers this seam
        cov = m[:,sx]
        rows=np.nonzero(cov)[0]
        if len(rows)>25:
            prof_e = L[rows][:,col].mean(0)
            prof_p = pl[rows][:,col].mean(0)
            ce = prof_e.max()-prof_e.min(); cp = prof_p.max()-prof_p.min()
            print("        seam x=%d over %4d rows: clean contrast %.1f -> under effect %.1f  %s"%(
                sx, len(rows), cp, ce, "OPAQUE" if ce<8 else ("partial" if ce<20 else "TRANSPARENT")))

print()
print("### LIFT — highest chunk top vs its ground shadow, per frame")
print(" px_per_world_unit horizontally = %.1f"%PXWU)
for fi in range(12,26):
    im=load(os.path.join(R2,'f%04d.jpg'%fi)); L=lum(im)
    d=np.abs(im-plate).max(-1); m=d>12
    warm=(im[...,0]-im[...,2])>10
    chunk=m&(L<140)&warm
    lab,n=ndi.label(chunk)
    if n==0: continue
    sz=ndi.sum(chunk,lab,range(1,n+1))
    keep=[k+1 for k,s in enumerate(sz) if s>=120]
    if not keep: continue
    ys,xs=np.nonzero(np.isin(lab,keep))
    ytop=ys.min()
    # shadows: darkening of the floor that is NOT part of a chunk (soft, desat, low delta)
    shad = m & (~chunk) & (pl-L>10) & (pl-L<70) & (np.abs(im[...,0]-im[...,2])<12)
    sy,sx_=np.nonzero(shad)
    sbot = sy.max() if len(sy)>50 else np.nan
    allm=np.nonzero(m)
    print("  t=%+0.3f  chunk_top_y=%4d  effect_bottom_y=%4d  vertical extent=%4dpx = %.2f cell = %.1f wu-equiv"%(
        (fi-T0)/FPS, ytop, allm[0].max(), allm[0].max()-ytop, (allm[0].max()-ytop)/CELL, (allm[0].max()-ytop)/PXWU))
