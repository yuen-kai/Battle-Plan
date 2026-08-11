import numpy as np, glob, os
from PIL import Image, ImageFilter
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1)
    return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)

print('=== INTERNAL LUMINANCE SPREAD inside the occluding mass (round-1 metric: ours 5.1, CM 30-45) ===')
for rd,f in (('r2',17),('r3',15),('r3',17),('r3',19)):
    D='Captures/AbilityJuice/shots/%s/shockwave/'%rd
    load=lambda i: np.asarray(Image.open(D+'f%04d.jpg'%i).convert('RGB')).astype(np.float32)
    plate=np.median(np.stack([load(i) for i in range(0,11)]),axis=0)
    a=load(f); La=lum(a); Lp=lum(plate)
    m=(np.abs(a-plate).max(axis=2)>20)&(La<Lp-25)
    v=La[m]
    # erode 8px so edge pixels don't inflate the spread
    im=Image.fromarray((m*255).astype(np.uint8)).filter(ImageFilter.MinFilter(17))
    core=np.asarray(im)>127
    vc=La[core]
    print('%s f%d  mass px=%d  L: mean %.0f std %.1f  p5-p95 %.0f-%.0f  | CORE(eroded 8px) n=%d std %.1f p5-p95 %.0f-%.0f'
          % (rd,f,m.sum(),v.mean(),v.std(),np.percentile(v,5),np.percentile(v,95),
             core.sum(), vc.std() if core.sum() else -1,
             np.percentile(vc,5) if core.sum() else -1, np.percentile(vc,95) if core.sum() else -1))

print()
print('=== CM reference: internal spread inside their dark/smoke masses ===')
for p in ['cm-clashabilities-04.jpg','cm-8newabilities-12.jpg','cm-8newabilities-19.jpg','cm-everyability-22.jpg']:
    a=np.asarray(Image.open('Captures/AbilityJuice/strips/reference/'+p).convert('RGB')).astype(np.float32)
    L=lum(a); h,w,_=a.shape; pw=w//3
    mid=L[:, pw:2*pw]
    # bright zone of the CM impact panel
    br = mid > 200
    print('%-30s impact panel: L mean %.0f std %.1f  bright(>200) frac %.2f  bright-zone L std %.1f'
          % (p, mid.mean(), mid.std(), br.mean(), mid[br].std() if br.any() else -1))

print()
print('=== HOT BAND AREA SHARE (r3 f17) ===')
D='Captures/AbilityJuice/shots/r3/shockwave/'
load=lambda i: np.asarray(Image.open(D+'f%04d.jpg'%i).convert('RGB')).astype(np.float32)
plate=np.median(np.stack([load(i) for i in range(0,11)]),axis=0)
a=load(17); La=lum(a); Sa=sat(a); Lp=lum(plate)
fx=np.abs(a-plate).max(axis=2)>18
for nm,m in [('effect footprint',fx),('L>200',fx&(La>200)),('L>200 & sat>0.45',fx&(La>200)&(Sa>0.45)),
             ('L>230',fx&(La>230)),('clipped any ch (>=250)',fx&(a.max(axis=2)>=250)),
             ('clipped ALL ch (white)',fx&(a.min(axis=2)>=250))]:
    print('  %-24s %7d px  = %.3f%% of 1024^2  = %.2f cells^2' % (nm, m.sum(), 100.0*m.sum()/1024**2, m.sum()/218.0**2))

print()
print('=== LIGHT SPILL: does the hot band illuminate the floor outside the dust? ===')
# floor pixels within 25px of the hot band but outside any effect matter
from PIL import ImageFilter as IF
hot = fx&(La>210)
hd = np.asarray(Image.fromarray((hot*255).astype(np.uint8)).filter(IF.MaxFilter(41)))>127
matter=(np.abs(a-plate).max(axis=2)>20)&(La<Lp-25)
ring = hd & (~matter) & (~hot)
print('  neighbour floor px:', int(ring.sum()))
print('  their mean L now %.1f vs plate %.1f  (delta %+.1f)' % (La[ring].mean(), Lp[ring].mean(), La[ring].mean()-Lp[ring].mean()))
print('  their mean sat now %.3f vs plate %.3f' % (Sa[ring].mean(), sat(plate)[ring].mean()))
