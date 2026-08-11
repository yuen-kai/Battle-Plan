import numpy as np, glob, os, json
from PIL import Image

def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1)
    return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)

def strip_stats(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    h,w,_ = a.shape
    S, L = sat(a), lum(a)
    keep = L > 8
    out = dict(file=os.path.basename(p), size=[w,h],
               sat_mean=round(float(S[keep].mean()),3),
               L_mean=round(float(L[keep].mean()),1))
    both = keep & (L>200) & (S>0.45)
    out['pct_bright_sat'] = round(100.0*both.sum()/keep.sum(),3)
    pw = w//3
    per = []
    for i in range(3):
        sl = slice(i*pw, (i+1)*pw)
        k = keep[:, sl]
        b = k & (L[:,sl]>200) & (S[:,sl]>0.45)
        per.append(round(100.0*b.sum()/max(k.sum(),1),3))
    out['panel_bright_sat'] = per
    return out

print('=== CLASH MINI REFERENCE (17 strips) ===')
refs = sorted(glob.glob('Captures/AbilityJuice/strips/reference/*.jpg'))
rs = [strip_stats(p) for p in refs]
for r in rs:
    print('%-30s  whole=%6.3f%%  panels=%s  satmean=%.3f' % (r['file'], r['pct_bright_sat'], r['panel_bright_sat'], r['sat_mean']))
bs = [r['pct_bright_sat'] for r in rs]
mid = [r['panel_bright_sat'][1] for r in rs]
print()
print('REF whole-strip bright&sat: min %.3f  median %.3f  max %.3f' % (min(bs), float(np.median(bs)), max(bs)))
print('REF IMPACT-PANEL bright&sat: min %.3f  median %.3f  max %.3f' % (min(mid), float(np.median(mid)), max(mid)))
print('REF sat_mean: min %.3f median %.3f max %.3f' % (min(r['sat_mean'] for r in rs), float(np.median([r['sat_mean'] for r in rs])), max(r['sat_mean'] for r in rs)))

print()
print('=== OURS ===')
for p in ['Captures/AbilityJuice/strips/r3/death.jpg',
          'Captures/AbilityJuice/strips/r4/death.jpg',
          'Captures/AbilityJuice/strips/r3/pogo.jpg',
          'Captures/AbilityJuice/strips/r4/pogo.jpg']:
    r = strip_stats(p)
    tag = p.split('/')[-2] + '/' + r['file']
    print('%-22s  whole=%6.3f%%  panels=%s  satmean=%.3f  Lmean=%.1f' % (tag, r['pct_bright_sat'], r['panel_bright_sat'], r['sat_mean'], r['L_mean']))
