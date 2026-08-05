import numpy as np
from PIL import Image
from scipy import ndimage

def panels(p):
    a=np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    h,w,_=a.shape; pw=w//3
    return [a[:,i*pw:(i+1)*pw] for i in range(3)]
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def satv(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)
def hue(a):
    r,g,b=a[...,0],a[...,1],a[...,2]; mx=a.max(axis=-1); mn=a.min(axis=-1); d=mx-mn
    h=np.zeros_like(mx); m=(d>1e-6)
    ir=m&(mx==r); ig=m&(mx==g)&~ir; ib=m&(mx==b)&~ir&~ig
    h[ir]=((g-b)[ir]/d[ir])%6; h[ig]=((b-r)[ig]/d[ig])+2; h[ib]=((r-g)[ib]/d[ib])+4
    return h*60

for rnd in ['r3','r4']:
    print('=== %s : dominant saturated hue of the CHANGED area, impact panel ==='%rnd.upper())
    for nm in ['death','pogo']:
        P=panels('Captures/AbilityJuice/strips/%s/%s.jpg'%(rnd,nm))
        d=np.abs(P[1]-P[0]).max(axis=-1); m=ndimage.binary_opening(d>34,np.ones((3,3)))
        m[:70,:]=False
        H,S,L=hue(P[1]),satv(P[1]),lum(P[1])
        sel=m&(S>0.35)
        hs=H[sel]
        hist,edges=np.histogram(hs,bins=36,range=(0,360))
        top=np.argsort(hist)[::-1][:3]
        print('  %-6s sat>0.35 px=%6d (%.1f%% of changed)  top hue bins: %s' %
              (nm, sel.sum(), 100*sel.sum()/max(m.sum(),1),
               ', '.join('%d-%d deg:%d'%(edges[t],edges[t+1],hist[t]) for t in top)))
        warm=sel&((H<60)|(H>330)); cool=sel&(H>150)&(H<250)
        print('         warm(orange/red) %6d px   cool(cyan/blue) %6d px   warm:cool = %.2f' %
              (warm.sum(),cool.sum(), warm.sum()/max(cool.sum(),1)))
        print('         mean sat=%.2f  mean L=%.0f   px both L>200 & S>0.45 = %d (%.3f%% of panel)' %
              (S[sel].mean(), L[sel].mean(), (m&(L>200)&(S>0.45)).sum(), 100*(m&(L>200)&(S>0.45)).sum()/m.size))
    print()

print('=== SHARED CYAN? cyan area in each r4 impact panel ===')
for nm in ['death','pogo']:
    P=panels('Captures/AbilityJuice/strips/r4/%s.jpg'%nm)
    d=np.abs(P[1]-P[0]).max(axis=-1); m=ndimage.binary_opening(d>34,np.ones((3,3))); m[:70,:]=False
    H,S,L=hue(P[1]),satv(P[1]),lum(P[1])
    cy=m&(H>160)&(H<220)&(S>0.35)
    print('  %-6s cyan px=%6d = %.1f%% of its changed area,  median L of that cyan = %.0f' %
          (nm,cy.sum(),100*cy.sum()/max(m.sum(),1), np.median(L[cy]) if cy.sum() else -1))
