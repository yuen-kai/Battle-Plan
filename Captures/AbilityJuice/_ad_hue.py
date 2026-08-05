import numpy as np
from PIL import Image
import os

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')
PITCH = {'death': 218.0, 'pogo': 158.0}
CEN = {'death': (510, 505), 'pogo': (765, 505)}


def load(shot, i):
    return np.asarray(Image.open(os.path.join(SH, shot, 'f%04d.jpg' % i)).convert('RGB')).astype(np.float32)


def lum(a):
    return 0.2126*a[..., 0]+0.7152*a[..., 1]+0.0722*a[..., 2]


def win(shot, a, cells, dy=0):
    p = PITCH[shot]; cx, cy = CEN[shot]; cy += dy; h = int(cells*p/2)
    H, W = a.shape[:2]
    return a[max(0, cy-h):min(H, cy+h), max(0, cx-h):min(W, cx+h)]


def hsv(px):
    px = px/255.0
    mx = px.max(1); mn = px.min(1); d = np.maximum(mx-mn, 1e-6)
    s = (mx-mn)/np.maximum(mx, 1e-4)
    r, g, b = px[:, 0], px[:, 1], px[:, 2]
    h = np.zeros_like(mx)
    k = (mx == r); h[k] = ((g-b)[k]/d[k]) % 6
    k = (mx == g) & ~(mx == r); h[k] = ((b-r)[k]/d[k])+2
    k = (mx == b) & ~(mx == r) & ~(mx == g); h[k] = ((r-g)[k]/d[k])+4
    return h*60, s, mx*255


print('opaque smoke/dust material only:  changed, 30 <= L <= 120, excludes badge and black shards')
print('%-12s %8s %7s %7s %7s %8s' % ('', 'n_px', 'hue25', 'hue50', 'hue75', 'sat50'))
buckets = {}
for shot, frames, cells in (('death', [13, 14, 16, 20, 26], 2.2), ('pogo', [45, 47, 48, 50, 58], 2.6)):
    for f in frames:
        a = load(shot, f); c = load(shot, 0)
        r = win(shot, a, cells); rc = win(shot, c, cells)
        L = lum(r)
        m = (np.abs(r-rc).max(-1) > 26) & (L >= 30) & (L <= 120)
        h, s, v = hsv(r[m])
        # drop near-neutrals from the hue stat, they have no meaningful hue
        hh = h[s > 0.10]
        buckets.setdefault(shot, []).append(hh)
        print('%-12s %8d %7.0f %7.0f %7.0f %8.3f' % (
            '%s f%d' % (shot, f), m.sum(),
            np.percentile(hh, 25), np.percentile(hh, 50), np.percentile(hh, 75), np.median(s)))

d = np.concatenate(buckets['death']); p = np.concatenate(buckets['pogo'])
print()
print('death material hue: median %.1f deg (IQR %.0f-%.0f)' % (np.median(d), np.percentile(d, 25), np.percentile(d, 75)))
print('pogo  material hue: median %.1f deg (IQR %.0f-%.0f)' % (np.median(p), np.percentile(p, 25), np.percentile(p, 75)))
print('separation: %.1f degrees of hue' % abs(np.median(d)-np.median(p)))
# overlap of the two hue distributions
lo, hi = 0, 120
hd, _ = np.histogram(d, bins=40, range=(lo, hi), density=True)
hp, _ = np.histogram(p, bins=40, range=(lo, hi), density=True)
bw = (hi-lo)/40
print('histogram overlap (0=disjoint, 1=identical): %.3f' % float(np.minimum(hd, hp).sum()*bw))
