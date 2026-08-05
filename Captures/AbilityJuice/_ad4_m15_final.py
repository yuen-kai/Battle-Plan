import numpy as np, glob, os
from PIL import Image
from scipy import ndimage

def panels(p):
    a=np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    h,w,_=a.shape; pw=w//3
    return [a[:,i*pw:(i+1)*pw] for i in range(3)]
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def satv(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)

print('=== 1. EFFECT FOOTPRINT as %% of the impact panel (plate = panel 1) ===')
for name,p in [('OURS death','Captures/AbilityJuice/strips/r4/death.jpg'),
               ('OURS pogo ','Captures/AbilityJuice/strips/r4/pogo.jpg')]:
    P=panels(p); d=np.abs(P[1]-P[0]).max(axis=-1)
    m=d>34
    m=ndimage.binary_opening(m,np.ones((3,3)))
    print('  %s  changed area = %6d px = %5.2f%% of panel' % (name,m.sum(),100*m.sum()/m.size))
for f in ['cm-8newabilities-12','cm-everyability-22','cm-clashabilities-04','cm-everyability-27']:
    P=panels('Captures/AbilityJuice/strips/reference/%s.jpg'%f)
    d=np.abs(P[1]-P[0]).max(axis=-1); m=ndimage.binary_opening(d>34,np.ones((3,3)))
    print('  CM %-22s changed area = %6d px = %5.2f%% of panel' % (f,m.sum(),100*m.sum()/m.size))

print()
print('=== 2. GREYSCALE SEPARATION of the two impact panels (effect region only) ===')
D=panels('Captures/AbilityJuice/strips/r4/death.jpg')
G=panels('Captures/AbilityJuice/strips/r4/pogo.jpg')
for nm,P in [('death',D),('pogo ',G)]:
    d=np.abs(P[1]-P[0]).max(axis=-1); m=ndimage.binary_opening(d>34,np.ones((3,3)))
    m[:70,:]=False
    L=lum(P[1]); S=satv(P[1])
    hist,_=np.histogram(L[m],bins=[0,40,80,120,160,200,256])
    hist=100*hist/max(m.sum(),1)
    print('  %s  L histogram %%  [0-40]=%.1f [40-80]=%.1f [80-120]=%.1f [120-160]=%.1f [160-200]=%.1f [200+]=%.1f' % ((nm,)+tuple(hist)))
    print('         L_mean=%.0f  L_median=%.0f  frac darker than board(<140)=%.1f%%  sat_mean=%.2f' %
          (L[m].mean(), np.median(L[m]), 100*(L[m]<140).mean(), S[m].mean()))
    # granularity: local 9x9 std inside the mass
    mean=ndimage.uniform_filter(L,9); sq=ndimage.uniform_filter(L*L,9)
    std=np.sqrt(np.maximum(sq-mean*mean,0))
    er=ndimage.binary_erosion(m,np.ones((7,7)))
    print('         interior local-9x9 std = %.2f  (Clash Mini 5.8-16.9)' % (std[er].mean() if er.sum() else -1))

print()
print('=== 3. CLIPPING + BRIGHT&SAT on the POGO frames (money-frame test) ===')
P='Captures/AbilityJuice/shots/r4/pogo'
for i in [47,48,49,50,51,52]:
    a=np.asarray(Image.open('%s/f%04d.jpg'%(P,i)).convert('RGB')).astype(np.float32)
    L=lum(a); S=satv(a)
    clip=((a[...,0]>=254)&(a[...,1]>=254)&(a[...,2]>=254)).sum()
    bs=((L>200)&(S>0.45)).sum()
    print('  f%02d %+0.3fs  clipped=%5d   bright&sat=%6d (%.3f%% of frame)   dark(L<110)=%6d' %
          (i,(i-49)/30.0,clip,bs,100*bs/(1024*1024),(L<110).sum()))
print()
print('  DEATH frames for the same test:')
D2='Captures/AbilityJuice/shots/r4/death'
for i in [15,16,17,18,19]:
    a=np.asarray(Image.open('%s/f%04d.jpg'%(D2,i)).convert('RGB')).astype(np.float32)
    L=lum(a); S=satv(a)
    clip=((a[...,0]>=254)&(a[...,1]>=254)&(a[...,2]>=254)).sum()
    bs=((L>200)&(S>0.45)).sum()
    print('  f%02d %+0.3fs  clipped=%5d   bright&sat=%6d (%.3f%% of frame)   dark(L<110)=%6d' %
          (i,(i-12)/30.0,clip,bs,100*bs/(1024*1024),(L<110).sum()))
