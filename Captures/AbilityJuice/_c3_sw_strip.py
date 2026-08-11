import numpy as np, glob, os, json
from PIL import Image

def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1)
    return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)

def strip_stats(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    h,w,_ = a.shape
    # strips are 3 panels; drop the black gutters
    S, L = sat(a), lum(a)
    keep = L > 8
    out = dict(file=os.path.basename(p), size=[w,h],
               sat_mean=round(float(S[keep].mean()),3),
               sat_p95=round(float(np.percentile(S[keep],95)),3),
               L_mean=round(float(L[keep].mean()),1))
    hi = keep & (L>200)
    out['pct_L200'] = round(100.0*hi.sum()/keep.sum(),2)
    both = keep & (L>200) & (S>0.45)
    out['pct_bright_sat'] = round(100.0*both.sum()/keep.sum(),3)
    # per-panel (thirds)
    pw = w//3
    per = []
    for i in range(3):
        sl = slice(i*pw, (i+1)*pw)
        k = keep[:, sl]
        b = k & (L[:,sl]>200) & (S[:,sl]>0.45)
        per.append(dict(sat=round(float(S[:,sl][k].mean()),3),
                        pct_bright_sat=round(100.0*b.sum()/max(k.sum(),1),3)))
    out['panels'] = per
    return out

print('=== CLASH MINI REFERENCE ===')
refs = sorted(glob.glob('Captures/AbilityJuice/strips/reference/*.jpg'))
rs = [strip_stats(p) for p in refs]
for r in rs: print(json.dumps(r))
sm = [r['sat_mean'] for r in rs]; bs = [r['pct_bright_sat'] for r in rs]
print('REF sat_mean range: %.3f - %.3f  median %.3f' % (min(sm), max(sm), float(np.median(sm))))
print('REF pct_bright_sat range: %.3f - %.3f  median %.3f' % (min(bs), max(bs), float(np.median(bs))))

print()
print('=== OURS ===')
for p in ['Captures/AbilityJuice/strips/r2/shockwave.jpg',
          'Captures/AbilityJuice/strips/r3/shockwave.jpg',
          'Captures/AbilityJuice/strips/r3/death.jpg',
          'Captures/AbilityJuice/strips/r3/pogo.jpg']:
    print(json.dumps(strip_stats(p)))
