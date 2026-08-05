import glob
import os

import numpy as np
from PIL import Image

W = np.array([0.2126, 0.7152, 0.0722], np.float32)


def panel2(path):
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
    if len(cuts) < 2:
        w = a.shape[1] // 3
        return a[:, w:2 * w]
    return a[:, cuts[0][1] + 1:cuts[1][0]]


def edge_hardness(p):
    """Step, in luminance per pixel, at the boundary of the largest white mass."""
    L = p @ W
    mn = p.min(axis=2)
    mx = p.max(axis=2)
    S = np.where(mx > 1, (mx - mn) / np.maximum(mx, 1), 0.0)
    wh = (mn >= 235) & (S < 0.10)
    if wh.sum() < 400:
        return None, 0, 0
    ys, xs = np.nonzero(wh)
    cy, cx = ys.mean(), xs.mean()
    R = int(min(p.shape[0], p.shape[1]) * 0.48)
    grads, widths = [], []
    for a in np.linspace(0, 2 * np.pi, 120, endpoint=False):
        rr = np.arange(0, R)
        yy = (cy + rr * np.sin(a)).astype(int)
        xx = (cx + rr * np.cos(a)).astype(int)
        ok = (yy >= 0) & (yy < p.shape[0]) & (xx >= 0) & (xx < p.shape[1])
        prof = L[yy[ok], xx[ok]]
        if len(prof) < 20 or prof[:4].mean() < 235:
            continue
        below = np.nonzero(prof < 200)[0]
        below = below[below > 3]
        if len(below) == 0:
            continue
        k = below[0]
        seg = prof[max(k - 8, 0):k + 10]
        if len(seg) < 6:
            continue
        grads.append(np.abs(np.diff(seg)).max())
        # 10-90 transition width
        hi, lo = prof[:4].mean(), prof[min(k + 12, len(prof) - 1)]
        span = hi - lo
        if span > 40:
            idx = np.nonzero((prof < hi - 0.1 * span))[0]
            idx2 = np.nonzero((prof < lo + 0.1 * span))[0]
            if len(idx) and len(idx2) and idx2[0] > idx[0]:
                widths.append(idx2[0] - idx[0])
    if not grads:
        return None, 0, 0
    return float(np.median(grads)), float(np.median(widths) if widths else 0), len(grads)


print('%-34s %9s %9s %7s' % ('impact panel', 'L/px', '10-90 px', 'panelW'))
ref = []
for f in sorted(glob.glob('Captures/AbilityJuice/strips/reference/*.jpg')):
    p = panel2(f)
    g, w, n = edge_hardness(p)
    if g is None:
        print('%-34s %9s' % (os.path.basename(f), 'no white mass'))
        continue
    ref.append((g, w))
    print('%-34s %9.1f %9.1f %7d' % (os.path.basename(f), g, w, p.shape[1]))

print()
for tag, f in [('OURS r2', 'Captures/AbilityJuice/strips/r2/impactcore.jpg'),
               ('OURS r3', 'Captures/AbilityJuice/strips/r3/impactcore.jpg')]:
    p = panel2(f)
    g, w, n = edge_hardness(p)
    print('%-34s %9.1f %9.1f %7d' % (tag, g, w, p.shape[1]))
r = np.array(ref)
print('\nreference median  %.1f L/px   %.1f px 10-90' % (np.median(r[:, 0]), np.median(r[:, 1])))
