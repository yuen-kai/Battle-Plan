import numpy as np, glob, os, json
from PIL import Image

D = 'Captures/AbilityJuice/shots/r2/hitreact'
files = sorted(glob.glob(os.path.join(D, 'f*.jpg')))
print('frames', len(files))
im0 = np.asarray(Image.open(files[0]).convert('RGB')).astype(np.float32)
print('size', im0.shape)

# Global per-frame stats to locate the flash
rows = []
for i, f in enumerate(files):
    a = np.asarray(Image.open(f).convert('RGB')).astype(np.float32)
    lum = 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]
    mx = a.max(2); mn = a.min(2)
    sat = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0)
    rows.append(dict(i=i, mean=float(lum.mean()), p999=float(np.percentile(lum, 99.9)),
                     nclip=int((a.min(2) >= 250).sum()),
                     nbright=int((lum > 230).sum()),
                     ndark=int((lum < 100).sum()),
                     satmean=float(sat.mean())))
for r in rows:
    print(f"{r['i']:3d} mean={r['mean']:7.2f} p999={r['p999']:6.1f} clip={r['nclip']:6d} bright={r['nbright']:6d} dark={r['ndark']:6d} sat={r['satmean']:.3f}")
json.dump(rows, open('Captures/AbilityJuice/_c3_hr_scan.json', 'w'))
