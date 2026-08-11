import numpy as np
from PIL import Image

P3 = 'Captures/AbilityJuice/shots/r3/impactcore/'
W = np.array([0.2126, 0.7152, 0.0722], np.float32)
PX = 218.0
CY, CX = 495, 506


def load(i):
    return np.asarray(Image.open(P3 + 'f%04d.jpg' % i)).astype(np.float32)


def sat(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    return np.where(mx > 1, (mx - mn) / np.maximum(mx, 1), 0.0)


print('--- edge hardness of the white core boundary (Clash Mini dust: 49-91 L/px) ---')
for i in [12, 14, 15, 16]:
    f = load(i)
    L = f @ W
    grads = []
    for a in np.linspace(0, 2 * np.pi, 180, endpoint=False):
        xs = CX + np.arange(0, 380) * np.cos(a)
        ys = CY + np.arange(0, 380) * np.sin(a)
        ok = (xs > 1) & (xs < 1022) & (ys > 1) & (ys < 1022)
        prof = L[ys[ok].astype(int), xs[ok].astype(int)]
        if prof.max() < 240:
            continue
        # first radius where the ray leaves the white plateau
        below = np.nonzero(prof < 235)[0]
        below = below[below > 5]
        if len(below) == 0:
            continue
        k = below[0]
        seg = prof[max(k - 6, 0):k + 8]
        if len(seg) > 3:
            grads.append(np.abs(np.diff(seg)).max())
    print(' f%02d t%+.3f  white-edge step: median %.1f L/px  p90 %.1f  (n=%d rays)' % (
        i, (i - 12) / 30.0, np.median(grads), np.percentile(grads, 90), len(grads)))

print('\n--- does the silhouette actually change during the plateau? ---')
prev = None
prevmask = None
for i in range(12, 18):
    f = load(i)
    mn = f.min(axis=2)
    wh = (mn >= 235) & (sat(f) < 0.10)
    ys, xs = np.nonzero(wh)
    cy, cx = ys.mean(), xs.mean()
    yy, xx = np.mgrid[0:1024, 0:1024]
    r = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2)
    th = np.arctan2(yy - cy, xx - cx)
    sig = []
    for a in np.linspace(-np.pi, np.pi, 72, endpoint=False):
        m = wh & (np.abs(((th - a + np.pi) % (2 * np.pi)) - np.pi) < np.pi / 72)
        sig.append(r[m].max() if m.sum() > 5 else 0.0)
    sig = np.array(sig)
    rough = sig.std() / max(sig.mean(), 1)
    line = 'f%02d t%+.3f  centroid (%.1f,%.1f)  radius mean %.0f px  raggedness %.3f' % (
        i, (i - 12) / 30.0, cy, cx, sig.mean(), rough)
    if prev is not None:
        drift = np.hypot(cy - prev[0], cx - prev[1]) / PX
        iou = (wh & prevmask).sum() / max((wh | prevmask).sum(), 1)
        xor = np.logical_xor(wh, prevmask).sum()
        line += '  | drift %.3f cells  IoU %.3f  changed px %d' % (drift, iou, xor)
    print(line)
    prev = (cy, cx)
    prevmask = wh
