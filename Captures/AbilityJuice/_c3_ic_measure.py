import numpy as np
from PIL import Image

DIR = 'Captures/AbilityJuice/shots/r3/impactcore/'
PX_PER_CELL = 218.0  # 1024px capture, impactcore camera (settled in round 2)


def load(i):
    return np.asarray(Image.open(DIR + 'f%04d.jpg' % i)).astype(np.float32)


def lum(a):
    return a @ np.array([0.2126, 0.7152, 0.0722], np.float32)


def sat(a):
    mx = a.max(axis=2)
    mn = a.min(axis=2)
    return np.where(mx > 1, (mx - mn) / np.maximum(mx, 1), 0.0)


plate = load(0)
Lp = lum(plate)

rows = []
for i in range(0, 46):
    f = load(i)
    L = lum(f)
    S = sat(f)
    mn = f.min(axis=2)

    clipped = (f[:, :, 0] >= 250) & (f[:, :, 1] >= 250) & (f[:, :, 2] >= 250)
    # white core: essentially achromatic and at/near ceiling
    white = (mn >= 235) & (S < 0.10)
    # body = anything materially different from the clean plate
    d = np.abs(f - plate).max(axis=2)
    body = d > 18

    rows.append(dict(i=i, t=(i - 12) * 1 / 30.0, clip=int(clipped.sum()),
                     white=int(white.sum()), body=int(body.sum()),
                     Lmax=float(L.max()), Smax95=float(np.percentile(S[body], 99)) if body.sum() > 50 else 0.0))

print('%-4s %-8s %8s %8s %8s %8s' % ('f', 't(s)', 'clip', 'white', 'body', 'Lmax'))
for r in rows:
    print('%-4d %+8.4f %8d %8d %8d %8.1f' % (r['i'], r['t'], r['clip'], r['white'], r['body'], r['Lmax']))
