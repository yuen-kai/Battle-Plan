import numpy as np
from PIL import Image

R4='Captures/AbilityJuice/shots/r4/death'
def A(i): return np.asarray(Image.open('%s/f%04d.jpg'%(R4,i)).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]

post = np.median(np.stack([A(i) for i in range(66,73)]),axis=0)
pre  = A(9)

print('=== EFFECT MASK vs post-plate (what is on screen that will not be there later) ===')
print('%4s %8s  %7s %7s  %6s %6s %6s %6s  %7s' % ('f','t','area','Ltop','x0','x1','y0','y1','h_px'))
prev=None
hist={}
for i in range(9,32):
    a=A(i); d=np.abs(a-post).max(axis=-1)
    m = d>30
    # kill the damage badge region (top-right of crop): badge is high-sat orange near y<330
    ys,xs=np.where(m)
    if len(ys)==0: continue
    # restrict to the death area column band
    sel = (xs>330)&(xs<760)&(ys>300)&(ys<720)
    ys,xs=ys[sel],xs[sel]
    mm=np.zeros_like(m); mm[ys,xs]=True
    hist[i]=mm
    L=lum(a)
    print('%4d %+8.3f  %7d %7.0f  %6d %6d %6d %6d  %7d' % (i,(i-12)/30.0,mm.sum(),L[mm].max() if mm.sum() else 0,
          xs.min(),xs.max(),ys.min(),ys.max(), ys.max()-ys.min()))
    prev=mm

print()
print('=== frame-to-frame IoU of the effect mask (builder claims worst 0.844) ===')
ks=sorted(hist)
ious=[]
for a_,b_ in zip(ks,ks[1:]):
    A1,B1=hist[a_],hist[b_]
    inter=(A1&B1).sum(); uni=(A1|B1).sum()
    v=inter/max(uni,1); ious.append((a_,b_,v))
    print('  f%02d->f%02d  IoU %.3f' % (a_,b_,v))
print('  WORST %.3f   worst in 12..26 window: %.3f' % (min(v for _,_,v in ious),
      min(v for a_,b_,v in ious if 12<=a_<=26)))

print()
print('=== VOID LUMINANCE (builder: 7 down the hole, 26 inner wall, 42 lip) ===')
for i in [16,18,20,22]:
    a=A(i); L=lum(a)
    m=hist[i]
    v=np.sort(L[m])
    print('  f%02d  min %.0f  p1 %.0f  p5 %.0f  p10 %.0f  p25 %.0f  median %.0f   px<20: %d  px<45: %d  px<110: %d' %
          (i, v[0], v[int(.01*len(v))], v[int(.05*len(v))], v[int(.10*len(v))], v[int(.25*len(v))], v[len(v)//2],
           (v<20).sum(), (v<45).sum(), (v<110).sum()))

print()
print('=== EDGE HARDNESS: luminance step per pixel across the silhouette boundary ===')
# horizontal scan through the mass centre at the money frame
for i in [18]:
    a=A(i); L=lum(a); m=hist[i]
    ys,xs=np.where(m); cy=int(np.median(ys))
    row=L[cy]
    print('  row y=%d, left edge scan:'%cy)
    xs_row=np.where(m[cy])[0]
    lx=xs_row.min(); rx=xs_row.max()
    print('   left  x=%d :'%lx, ' '.join('%3.0f'%row[x] for x in range(lx-12,lx+13)))
    print('   right x=%d :'%rx, ' '.join('%3.0f'%row[x] for x in range(rx-12,rx+13)))
    grad=np.abs(np.diff(row))
    print('   max |dL/dx| within 15px of left edge : %.1f' % grad[max(lx-15,0):lx+15].max())
    print('   max |dL/dx| within 15px of right edge: %.1f' % grad[rx-15:rx+15].max())
