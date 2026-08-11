import numpy as np, json
from PIL import Image

D = 'Captures/AbilityJuice/shots/r3/shockwave/'

def load(i):
    return np.asarray(Image.open(D + 'f%04d.jpg' % i).convert('RGB')).astype(np.float32)

# clean plate from pre-effect frames
plate = np.median(np.stack([load(i) for i in range(0, 11)]), axis=0)
np.save('Captures/AbilityJuice/_c3_sw_plate.npy', plate)

def lum(a):
    return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]

Lp = lum(plate)
print('plate lum: mean %.1f  p05 %.1f  p50 %.1f  p95 %.1f' % (Lp.mean(), np.percentile(Lp,5), np.percentile(Lp,50), np.percentile(Lp,95)))

rows = []
for i in range(0, 56):
    a = load(i)
    d = np.abs(a - plate).max(axis=2)
    m = d > 18
    L = lum(a)
    area = int(m.sum())
    if area > 200:
        ys, xs = np.nonzero(m)
        cy, cx = ys.mean(), xs.mean()
        dark = int(((L < 150) & m).sum())
    else:
        cy = cx = -1; dark = 0
    rows.append(dict(f=i, t=round(i*(1/30.0) - 0.4, 4), area=area, dark=dark, cy=round(float(cy),1), cx=round(float(cx),1),
                     maxL=round(float(L[m].max()) if area else 0, 1)))
    print(rows[-1])

json.dump(rows, open('Captures/AbilityJuice/_c3_sw_rows.json','w'), indent=1)
