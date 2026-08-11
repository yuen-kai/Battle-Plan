import numpy as np
from PIL import Image
from scipy import ndimage

R4='Captures/AbilityJuice/shots/r4/death'
def A(i): return np.asarray(Image.open('%s/f%04d.jpg'%(R4,i)).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]

CELL=228.0   # px per 2.7-unit cell in the 1024 capture (measured from tile seams)
post = np.median(np.stack([A(i) for i in range(66,73)]),axis=0)
Lpost = lum(post)

print('=== LARGEST CONNECTED DARK MASS (L<60) that is NOT pre-existing furniture ===')
print('%4s %8s %8s %7s %7s %7s %7s %6s' % ('f','t','area_px','cells2','w_cells','h_cells','h/w','solid'))
for i in [13,15,16,17,18,20,22,24]:
    a=A(i); L=lum(a)
    new = (L<60) & (Lpost>100)          # dark now, floor later -> not furniture
    lab,n = ndimage.label(new)
    if n==0: continue
    sizes=ndimage.sum(new,lab,range(1,n+1))
    k=int(np.argmax(sizes))+1
    m=(lab==k)
    ys,xs=np.where(m)
    w=xs.max()-xs.min()+1; h=ys.max()-ys.min()+1
    print('%4d %+8.3f %8d %7.2f %7.2f %7.2f %7.2f %6.2f' %
          (i,(i-12)/30.0,m.sum(),m.sum()/CELL**2,w/CELL,h/CELL,h/w,m.sum()/(w*h)))

print()
print('=== OCCLUSION TEST: tile-seam contrast under the mass (clean seam ~39, opaque must be <8) ===')
# seams along y=640 at x = 164, 392, 620, 847 ; vertical seams too. Sample a seam that runs under the mass.
a=A(18); L=lum(a)
print('  clean floor seam contrast (f0, x=620 column, y 700..760):')
a0=A(0); L0=lum(a0)
seg=L0[700:760, 610:632]
print('   values across seam:', ' '.join('%3.0f'%v for v in L0[730,610:632]))
print('   contrast(max-min) = %.0f' % (L0[730,610:632].max()-L0[730,610:632].min()))
print('  same seam under the death mass at f18 (y ~ 500):')
for y in [470,500,530]:
    v=L[y,610:632]
    print('   y=%d :'%y, ' '.join('%3.0f'%x for x in v), ' contrast=%.0f'%(v.max()-v.min()))

print()
print('=== DOES THE MASS SIT ON THE FLOOR OR FLOAT? vertical extent vs the unit footprint ===')
print('  unit stood at y 417..560 (feet ~560, head ~417), 1 cell = 228px wide')
for i in [16,18,20]:
    a=A(i); L=lum(a)
    new=(L<60)&(Lpost>100)
    lab,n=ndimage.label(new); sizes=ndimage.sum(new,lab,range(1,n+1))
    m=(lab==int(np.argmax(sizes))+1); ys,xs=np.where(m)
    print('  f%02d mass y %d..%d  (%.0f px ABOVE the head, %.0f px BELOW the feet)' %
          (i,ys.min(),ys.max(), 417-ys.min(), ys.max()-560))
