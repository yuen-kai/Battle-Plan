import numpy as np, json
from PIL import Image

PX_PER_CELL = 218.0   # settled in MEASURED_FINDINGS for the 1024 capture
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]

def run(rd, lo, hi):
    D='Captures/AbilityJuice/shots/%s/shockwave/'%rd
    load=lambda i: np.asarray(Image.open(D+'f%04d.jpg'%i).convert('RGB')).astype(np.float32)
    plate=np.median(np.stack([load(i) for i in range(0,11)]),axis=0); Lp=lum(plate)
    rows=[]; prev=None
    for i in range(lo,hi):
        a=load(i); La=lum(a)
        m=(np.abs(a-plate).max(axis=2)>20)&(La<Lp-25)
        n=int(m.sum())
        if n<150:
            rows.append(dict(f=i,t=round(i/30.0-0.4,3),mass=0.0)); prev=None; continue
        ys,xs=np.nonzero(m); cy,cx=ys.mean(),xs.mean()
        mass=n/(PX_PER_CELL**2)
        d=None
        if prev is not None:
            d=round(float(np.hypot(cy-prev[0],cx-prev[1])/PX_PER_CELL),4)
        rows.append(dict(f=i,t=round(i/30.0-0.4,3),mass=round(mass,3),
                         cy=round(float(cy),1),cx=round(float(cx),1),drift_cells=d,
                         bbox_w=round(float(xs.max()-xs.min())/PX_PER_CELL,2),
                         bbox_h=round(float(ys.max()-ys.min())/PX_PER_CELL,2),
                         darkest=round(float(La[m].min()),0),
                         medL=round(float(np.median(La[m])),0)))
        prev=(cy,cx)
    return rows

print('=== r3 TAIL (mass in cells^2, drift in cells/frame) ===')
r3=run('r3',18,38)
for r in r3: print(r)
d=[r['drift_cells'] for r in r3 if r.get('drift_cells')]
mass=[r['mass'] for r in r3 if r['mass']>0]
mono=all(mass[i]>=mass[i+1]-1e-9 for i in range(len(mass)-1))
print('drift: min %.4f max %.4f mean %.4f' % (min(d),max(d),float(np.mean(d))))
print('mass first->last: %.3f -> %.3f   monotonic decline: %s' % (mass[0],mass[-1],mono))
print('mass sequence:', mass)

print()
print('=== r2 TAIL for comparison ===')
r2=run('r2',18,38)
d2=[r['drift_cells'] for r in r2 if r.get('drift_cells')]
m2=[r['mass'] for r in r2 if r['mass']>0]
print('drift: min %.4f max %.4f mean %.4f' % (min(d2),max(d2),float(np.mean(d2))))
print('mass sequence:', m2)

# byte-identical frame check
print()
import hashlib
for rd in ('r2','r3'):
    hs={}
    for i in range(12,40):
        p='Captures/AbilityJuice/shots/%s/shockwave/f%04d.jpg'%(rd,i)
        h=hashlib.md5(open(p,'rb').read()).hexdigest()
        hs.setdefault(h,[]).append(i)
    dup={k:v for k,v in hs.items() if len(v)>1}
    print(rd,'duplicate frames:',list(dup.values()) or 'none')

# MASS LEAN: split matter about the origin, heavy side vs light side
def lean(rd,f):
    D='Captures/AbilityJuice/shots/%s/shockwave/'%rd
    load=lambda i: np.asarray(Image.open(D+'f%04d.jpg'%i).convert('RGB')).astype(np.float32)
    plate=np.median(np.stack([load(i) for i in range(0,11)]),axis=0); Lp=lum(plate)
    a=load(f); La=lum(a)
    m=(np.abs(a-plate).max(axis=2)>20)&(La<Lp-25)
    ys,xs=np.nonzero(m); oy,ox=ys.mean(),xs.mean()
    th=np.arctan2(ys-oy,xs-ox)
    best=0; bestang=0
    for ang in np.linspace(0,np.pi,180,endpoint=False):
        d=np.cos(th-ang)
        A=(d>0).sum(); B=(d<=0).sum()
        r=max(A,B)/max(min(A,B),1)
        if r>best: best,bestang=r,ang
    return round(float(best),2), round(float(np.degrees(bestang)),0)
for rd,f in (('r2',17),('r3',17)):
    print(rd,'f%d mass lean (heavy:light, axis deg) ='%f, lean(rd,f))
