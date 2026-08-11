import glob
import os

import numpy as np
from PIL import Image

W = np.array([0.2126, 0.7152, 0.0722], np.float32)


def panels(path):
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    dark = (a.max(axis=2) < 14).mean(axis=0) > 0.9
    cuts, run = [], []
    for x, v in enumerate(dark):
        if v:
            run.append(x)
        elif run:
            if len(run) >= 3:
                cuts.append((run[0], run[-1]))
            run = []
    if run and len(run) >= 3:
        cuts.append((run[0], run[-1]))
    cuts = [c for c in cuts if 0.15 * a.shape[1] < c[0] < 0.85 * a.shape[1]]
    if len(cuts) >= 2:
        b = [0, cuts[0][0], cuts[0][1] + 1, cuts[1][0], cuts[1][1] + 1, a.shape[1]]
    else:
        w = a.shape[1] // 3
        b = [0, w, w, 2 * w, 2 * w, a.shape[1]]
    return a, [a[:, b[0]:b[1]], a[:, b[2]:b[3]], a[:, b[4]:b[5]]]


def stats(p):
    L = p @ W
    mx, mn = p.max(axis=2), p.min(axis=2)
    S = np.where(mx > 1, (mx - mn) / np.maximum(mx, 1), 0.0)
    clip = (p[:, :, 0] >= 250) & (p[:, :, 1] >= 250) & (p[:, :, 2] >= 250)
    white = (mn >= 235) & (S < 0.10)
    hot_sat = (L > 170) & (S > 0.45)
    wD = 2 * np.sqrt(white.sum() / np.pi)
    cD = 2 * np.sqrt(clip.sum() / np.pi)
    return dict(w=p.shape[1], whitePct=100.0 * wD / p.shape[1], clipPct=100.0 * clip.mean(),
                clipDPct=100.0 * cD / p.shape[1], hotsat=100.0 * hot_sat.mean(),
                floor=float(np.median(L)))


print('%-34s %8s %8s %8s %8s %8s' % ('strip (impact panel)', 'whiteD%', 'clipD%', 'clip%', 'hotsat%', 'floorL'))
rows = []
for f in sorted(glob.glob('Captures/AbilityJuice/strips/reference/*.jpg')):
    _, ps = panels(f)
    s = stats(ps[1])
    rows.append((os.path.basename(f), s))
    print('%-34s %8.1f %8.1f %8.2f %8.2f %8.1f' % (os.path.basename(f), s['whitePct'], s['clipDPct'], s['clipPct'], s['hotsat'], s['floor']))

print()
for tag, f in [('OURS r2', 'Captures/AbilityJuice/strips/r2/impactcore.jpg'),
               ('OURS r3', 'Captures/AbilityJuice/strips/r3/impactcore.jpg')]:
    _, ps = panels(f)
    s = stats(ps[1])
    print('%-34s %8.1f %8.1f %8.2f %8.2f %8.1f' % (tag, s['whitePct'], s['clipDPct'], s['clipPct'], s['hotsat'], s['floor']))

r = np.array([[x[1]['whitePct'], x[1]['clipDPct'], x[1]['clipPct'], x[1]['hotsat']] for x in rows])
print('\nreference median %.1f %.1f %.2f %.2f   max %.1f %.1f %.2f %.2f' % (
    *np.median(r, axis=0), *r.max(axis=0)))
