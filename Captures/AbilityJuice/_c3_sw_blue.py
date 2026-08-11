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

print('--- THEORY: max Rec.709 luminance achievable at a given saturation, per hue ---')
for h in (170,180,190,200,210,220,240):
    for s in (0.45,0.55,0.65):
        mx=255.0; mn=mx*(1-s); c=mx-mn
        hp=h/60.0; x=c*(1-abs(hp%2-1))
        if 2<=hp<3: r,g,b=0,c,x
        elif 3<=hp<4: r,g,b=0,x,c
        else: r,g,b=0,0,c
        r,g,b = r+mn, g+mn, b+mn
        L=0.2126*r+0.7152*g+0.0722*b
        print('  hue %3d  sat %.2f  ->  RGB(%3.0f,%3.0f,%3.0f)  L=%5.1f' % (h,s,r,g,b,L))

print()
print('--- EMPIRICAL: bright+saturated COOL pixels in Clash Mini references ---')
tot = []
for p in sorted(glob.glob('Captures/AbilityJuice/strips/reference/*.jpg')):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    L,S,H = lum(a), sat(a), hue(a)
    cool = (H>=160)&(H<=270)
    br = (L>200)&(S>0.45)
    n = int((cool&br).sum())
    n180 = int((cool&(L>180)&(S>0.45)).sum())
    row = dict(f=os.path.basename(p), cool_bright_sat=n, cool_L180_sat=n180)
    if n>0:
        sel = cool&br
        row['maxL_of_cool_sat'] = round(float(L[sel].max()),0)
        row['hue_med'] = round(float(np.median(H[sel])),0)
        row['sat_med'] = round(float(np.median(S[sel])),2)
    # what is the max luminance reached by ANY pixel with sat>0.45 and hue in 160-270
    cs = cool&(S>0.45)
    if cs.any():
        row['cool_sat_maxL'] = round(float(L[cs].max()),0)
        row['cool_sat_p99L'] = round(float(np.percentile(L[cs],99)),0)
    tot.append(row); print(json.dumps(row))

print()
print('--- OURS r3 f17: same query ---')
a = np.asarray(Image.open('Captures/AbilityJuice/shots/r3/shockwave/f0017.jpg').convert('RGB')).astype(np.float32)
L,S,H = lum(a), sat(a), hue(a)
cool=(H>=160)&(H<=270); cs=cool&(S>0.45)
print('cool sat>0.45 px:', int(cs.sum()), ' maxL:', round(float(L[cs].max()),0), ' p99L:', round(float(np.percentile(L[cs],99)),0))
print('cool sat>0.45 AND L>200:', int((cs&(L>200)).sum()))
print('cool sat>0.45 AND L>180:', int((cs&(L>180)).sum()))
print('hue of our cool sat px: p10/p50/p90 =', [round(float(np.percentile(H[cs],q)),0) for q in (10,50,90)])
