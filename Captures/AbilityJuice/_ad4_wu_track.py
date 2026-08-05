import numpy as np, glob, os
from PIL import Image

def lum(x): return 0.2126*x[...,0]+0.7152*x[...,1]+0.0722*x[...,2]
def sat(x):
    mx=x.max(-1); mn=x.min(-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0)
def hue(x):
    mx=x.max(-1); mn=x.min(-1); d=mx-mn
    r,g,b=x[...,0],x[...,1],x[...,2]
    h=np.zeros_like(mx)
    m=(d>1e-6)&(mx==r); h[m]=(60*((g-b)[m]/d[m]))%360
    m=(d>1e-6)&(mx==g); h[m]=60*((b-r)[m]/d[m])+120
    m=(d>1e-6)&(mx==b); h[m]=60*((r-g)[m]/d[m])+240
    return h

fs = sorted(glob.glob('Captures/AbilityJuice/shots/r4/windup/f*.jpg'))
print('frames', len(fs), 'size', Image.open(fs[0]).size)

# caster is the LEFT unit. restrict to left half to avoid the target.
A0 = np.asarray(Image.open(fs[0]).convert('RGB')).astype(np.float32)
H,W,_ = A0.shape
LEFT = slice(0, int(W*0.55))

print('\n frame   t      castX   castY   dX(px)  | chargeArea chargeW chargeH  cx   medS  clipR clipAll')
prev = None
rows=[]
for i,f in enumerate(fs):
    A = np.asarray(Image.open(f).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    # caster head: strong pink/magenta, hue 320-360, sat>0.4, left half
    pink = (S>0.35)&(((Hh>315)|(Hh<10)))&(L>60)&(L<200)
    pm = np.zeros_like(pink); pm[:, LEFT] = pink[:, LEFT]
    # keep the largest blob-ish: use the leftmost cluster by taking pixels within 60px of the min-x
    ys,xs = np.nonzero(pm)
    if len(xs)==0:
        cx=cy=np.nan
    else:
        x0 = xs.min()
        sel = xs < x0+90
        cx, cy = xs[sel].mean(), ys[sel].mean()
    ch = (L>200)&(S>0.45)&(Hh>25)&(Hh<75)
    chm = np.zeros_like(ch); chm[:, LEFT] = ch[:, LEFT]
    area = int(chm.sum())
    if area>5:
        cys,cxs = np.nonzero(chm)
        cw_, chh_ = cxs.max()-cxs.min()+1, cys.max()-cys.min()+1
        ccx = cxs.mean()
        ms = float(np.median(S[chm]))
    else:
        cw_=chh_=0; ccx=np.nan; ms=0.0
    clipR = int(((A[...,0]>=254)).sum()); clipAll = int((A.min(-1)>=254).sum())
    t = i/30.0 - 0.4
    rows.append((i,t,cx,cy,area,cw_,chh_,ccx,ms,clipR,clipAll))

base = rows[0][2]
for (i,t,cx,cy,area,cw_,chh_,ccx,ms,clipR,clipAll) in rows:
    mark = ''
    if i in (22,40,54): mark = '  <== STRIP PANEL'
    if i==45: mark += '  <== FIRE t=1.10'
    print('%5d %+6.3f  %6.1f %6.1f  %+6.1f  | %8d %6d %6d %6.1f %5.2f %5d %5d%s' % (
        i,t,cx,cy,(cx-base),area,cw_,chh_,ccx,ms,clipR,clipAll,mark))
