import numpy as np
from PIL import Image

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

print('=== VOID SHAPE at the money frame (f17/f18) — is it a hole in a plane? ===')
for i in [16,18,20]:
    a=A(i); L=lum(a)
    m=(L<45)
    m[:360,:]=False   # kill badge outline & top furniture
    m[:, :330]=False
    ys,xs=np.where(m)
    w=xs.max()-xs.min(); h=ys.max()-ys.min()
    print(' f%02d  dark bbox x%d..%d (w=%d)  y%d..%d (h=%d)  aspect h/w=%.2f  area=%d  fill=%.2f'
          % (i,xs.min(),xs.max(),w,ys.min(),ys.max(),h,h/w,m.sum(), m.sum()/(w*h)))

print()
print('  A circle drawn flat on this floor should project to h/w = cos(camera tilt).')
# measure the floor grid to get the projection ratio
a=A(0); L=lum(a)
# tile seams: find dark-ish lines. Use gradient peaks along a column and a row in clean floor
col=L[380:760, 250]; row=L[640, 120:900]
def seams(v, thr=6):
    g=np.abs(np.diff(v)); return list(np.where(g>thr)[0])
print('  vertical seam positions along x=250 (y380..760):', seams(col))
print('  horizontal seam positions along y=640 (x120..900):', seams(row))

print()
print('=== VERTICAL LUMINANCE PROFILE THROUGH THE VOID (a hole is bright far-wall at top, black at bottom) ===')
i=18; a=A(i); L=lum(a); S=satv(a); H=hue(a)
m=(L<80); m[:360,:]=False; m[:,:330]=False
ys,xs=np.where(m); cx=int(np.median(xs))
print('  column x=%d,  y : L' % cx)
prof=[]
for y in range(390,700,10):
    prof.append((y, L[y,cx]))
print('  ', ' '.join('%d:%.0f'%(y,v) for y,v in prof))

print()
print('=== CYAN: is it BRIGHT and SATURATED, and how much AREA? (1024px frame) ===')
print('%4s %8s %9s %9s %9s %9s %9s' % ('f','t','cyan_area','%frame','sat_mean','L_mean','L_p95'))
for i in [14,16,17,18,20,22]:
    a=A(i); L=lum(a); S=satv(a); H=hue(a)
    cy=(H>150)&(H<230)&(S>0.45); cy[:360,:]=False; cy[:,:330]=False
    if cy.sum()==0: continue
    print('%4d %+8.3f %9d %9.3f %9.3f %9.1f %9.0f' % (i,(i-12)/30.0,cy.sum(),100*cy.sum()/(1024*1024),
          S[cy].mean(), L[cy].mean(), np.percentile(L[cy],95)))
    both=cy&(L>200)
    print('       -> of those, L>200 (bright AND saturated): %d px = %.4f%% of frame' % (both.sum(),100*both.sum()/(1024*1024)))
    hs=H[cy&(L>150)]
    if len(hs): print('       -> hue of the brighter cyan: median %.0f deg  p5 %.0f  p95 %.0f' % (np.median(hs),np.percentile(hs,5),np.percentile(hs,95)))

print()
print('=== the builder claims "cyan at hue 189, saturation 1.00". Peak-pixel check: ===')
a=A(18); L=lum(a); S=satv(a); H=hue(a)
cy=(H>150)&(H<230); cy[:360,:]=False; cy[:,:330]=False
br=cy&(L>np.percentile(L[cy],99.5))
print('  brightest 0.5%% of cyan pixels: n=%d  L_mean=%.0f  sat_mean=%.3f  hue_med=%.0f' % (br.sum(),L[br].mean(),S[br].mean(),np.median(H[br])))
print('  their RGB mean:', a[br].mean(axis=0).round(1))
