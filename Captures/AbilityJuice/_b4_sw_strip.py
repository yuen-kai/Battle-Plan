import numpy as np, glob, os, json
from PIL import Image

def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1)
    return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)
def hue(a):
    r,g,b = a[...,0],a[...,1],a[...,2]
    mx=a.max(axis=-1); mn=a.min(axis=-1); d=mx-mn
    h=np.zeros_like(mx); m=d>1e-6
    ir=(mx==r)&m; ig=(mx==g)&m; ib=(mx==b)&m
    h[ir]=(((g-b)[ir]/d[ir])%6); h[ig]=((b-r)[ig]/d[ig])+2; h[ib]=((r-g)[ib]/d[ib])+4
    return h*60

def report(p, tag):
    a=np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    L,S,H=lum(a),sat(a),hue(a)
    bs=(L>200)&(S>0.45)
    n=a.shape[0]*a.shape[1]
    row='%-34s %4dx%-4d  bright&sat %6d = %.3f%%  meanS %.3f' % (tag,a.shape[1],a.shape[0],bs.sum(),100.0*bs.sum()/n,S.mean())
    if bs.sum()>30:
        row += '  hueMed %3.0f  Lp95 %3.0f' % (np.median(H[bs]), np.percentile(L[bs],95))
    print(row)
    return 100.0*bs.sum()/n

print('=== OURS ===')
for r in ('r2','r3'):
    report('Captures/AbilityJuice/strips/%s/shockwave.jpg'%r, r+'/shockwave')

print('\n=== CLASH MINI REFERENCE ===')
vals=[]
for p in sorted(glob.glob('Captures/AbilityJuice/strips/reference/*.jpg')):
    vals.append(report(p, os.path.basename(p)))
vals=np.array(vals)
print('\nreference bright&sat%%: min %.3f  median %.3f  max %.3f' % (vals.min(), np.median(vals), vals.max()))

# how much area, in strip pixels, is 1.5% of a panel?
a=np.asarray(Image.open('Captures/AbilityJuice/strips/r3/shockwave.jpg').convert('RGB'))
h,w,_=a.shape; pw=w//3
print('\nstrip %dx%d  panel %dx%d = %d px   1.5%% of whole strip = %d px   1.5%% of one panel = %d px'
      % (w,h,pw,h,pw*h, int(0.015*w*h), int(0.015*pw*h)))
print('shot is 1024x1024; panel is %dx%d -> downscale factor %.2f, area factor %.3f' % (pw,h,pw/1024.0,(pw/1024.0)**2))
print('=> %d px on the panel corresponds to ~%d px in the 1024^2 shot' % (int(0.015*pw*h), int(0.015*pw*h/ (pw/1024.0)**2)))
