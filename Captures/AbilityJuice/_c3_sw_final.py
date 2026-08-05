import numpy as np, json
from PIL import Image
PC=218.0
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def mat(rd,f):
    D='Captures/AbilityJuice/shots/%s/shockwave/'%rd
    load=lambda i: np.asarray(Image.open(D+'f%04d.jpg'%i).convert('RGB')).astype(np.float32)
    plate=np.median(np.stack([load(i) for i in range(0,11)]),axis=0)
    a=load(f); La=lum(a); Lp=lum(plate)
    return (np.abs(a-plate).max(axis=2)>20)&(La<Lp-25), a, plate

# spawn origin = centroid of the very first frame the effect appears
def spawn(rd):
    m,_,_ = mat(rd,12)
    ys,xs=np.nonzero(m); return ys.mean(), xs.mean()

print('=== MASS LEAN measured about the SPAWN ORIGIN (not the mass centroid) ===')
for rd in ('r2','r3'):
    oy,ox = spawn(rd)
    m,_,_ = mat(rd,17)
    ys,xs=np.nonzero(m); th=np.arctan2(ys-oy,xs-ox)
    best=0;ba=0
    for ang in np.linspace(0,np.pi,180,endpoint=False):
        d=np.cos(th-ang); A=int((d>0).sum()); B=int((d<=0).sum())
        r=max(A,B)/max(min(A,B),1)
        if r>best: best,ba=r,ang
    cy,cx=ys.mean(),xs.mean()
    off=np.hypot(cy-oy,cx-ox)
    print('%s spawn=(%.0f,%.0f) lean=%.2f:1 @%.0fdeg   centroid offset from spawn = %.0fpx = %.2f cells'
          % (rd,oy,ox,best,np.degrees(ba),off,off/PC))

print()
print('=== COLLAR (inner hole) OFFSET vs outer boundary centre ===')
def collar(rd,f):
    m,_,_=mat(rd,f)
    filled = m.copy()
    # flood outer background from border, anything unreached & not matter = hole
    h,w=m.shape
    lab=np.zeros_like(m,dtype=np.int32)
    from collections import deque
    seen=np.zeros_like(m,dtype=bool)
    dq=deque()
    for x in range(w):
        for y in (0,h-1):
            if not m[y,x] and not seen[y,x]: seen[y,x]=True; dq.append((y,x))
    for y in range(h):
        for x in (0,w-1):
            if not m[y,x] and not seen[y,x]: seen[y,x]=True; dq.append((y,x))
    while dq:
        y,x=dq.popleft()
        for dy,dx in ((1,0),(-1,0),(0,1),(0,-1)):
            ny,nx=y+dy,x+dx
            if 0<=ny<h and 0<=nx<w and not m[ny,nx] and not seen[ny,nx]:
                seen[ny,nx]=True; dq.append((ny,nx))
    hole = (~m)&(~seen)
    ys,xs=np.nonzero(m); ocy,ocx=ys.mean(),xs.mean()
    outer_r = np.sqrt(m.sum()/np.pi)
    if hole.sum()<200: return None
    hy,hx=np.nonzero(hole)
    d=np.hypot(hy.mean()-ocy,hx.mean()-ocx)
    return dict(hole_px=int(hole.sum()), hole_c=[round(float(hy.mean()),0),round(float(hx.mean()),0)],
                outer_c=[round(float(ocy),0),round(float(ocx),0)],
                offset_px=round(float(d),1), equiv_R=round(float(outer_r),0),
                offset_pct_of_R=round(100.0*d/outer_r,1))
for rd in ('r2','r3'):
    print(rd, collar(rd,17))

print()
print('=== r2 TAIL rows (the "frozen" window +0.333 to +0.533) ===')
D='Captures/AbilityJuice/shots/r2/shockwave/'
load=lambda i: np.asarray(Image.open(D+'f%04d.jpg'%i).convert('RGB')).astype(np.float32)
plate=np.median(np.stack([load(i) for i in range(0,11)]),axis=0); Lp=lum(plate)
prev=None
for i in range(20,34):
    a=load(i); La=lum(a); m=(np.abs(a-plate).max(axis=2)>20)&(La<Lp-25)
    ys,xs=np.nonzero(m); cy,cx=ys.mean(),xs.mean()
    d=np.hypot(cy-prev[0],cx-prev[1])/PC if prev else None
    print(' f%d t=%+.3f mass=%.3f bbox=%.2fx%.2f drift=%s' % (i, i/30.0-0.4, m.sum()/PC**2,
          (xs.max()-xs.min())/PC,(ys.max()-ys.min())/PC, ('%.4f'%d) if d else '-'))
    prev=(cy,cx)
