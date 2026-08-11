import numpy as np
from PIL import Image
import os

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')
PITCH = {'death': 218.0, 'pogo': 158.0}
CEN = {'death': (510, 490), 'pogo': (765, 500)}


def load(shot, i):
    return np.asarray(Image.open(os.path.join(SH, shot, 'f%04d.jpg' % i)).convert('RGB')).astype(np.float32)


def lum(a):
    return 0.2126*a[..., 0]+0.7152*a[..., 1]+0.0722*a[..., 2]


def win(shot, a, cells):
    p = PITCH[shot]; cx, cy = CEN[shot]; h = int(cells*p/2)
    H, W = a.shape[:2]
    return a[max(0, cy-h):min(H, cy+h), max(0, cx-h):min(W, cx+h)]


print('=== DEATH: ROI-limited silhouette stability (3.2-cell window) ===')
p = PITCH['death']
clean = load('death', 0)
prev_mask = None
print(' t       area(c^2)  w     h     cx     cy    dcent(cells)  IoU(prev)')
for i in range(12, 40):
    a = load('death', i)
    r = win('death', a, 3.2); rc = win('death', clean, 3.2)
    m = (np.abs(r-rc).max(-1) > 26) & (lum(r) < 120)
    ys, xs = np.nonzero(m)
    if len(xs) < 50:
        continue
    area = m.sum()/(p*p)
    cx, cy = xs.mean(), ys.mean()
    iou = np.nan
    if prev_mask is not None:
        inter = (m & prev_mask).sum(); uni = (m | prev_mask).sum()
        iou = inter/max(uni, 1)
    if 'pc' in dir():
        dc = np.hypot(cx-pc[0], cy-pc[1])/p
    else:
        dc = np.nan
    print('%+0.3f   %7.3f  %5.2f %5.2f %6.1f %6.1f     %7.4f     %6.3f' % (
        (i-13)/30.0, area, (xs.max()-xs.min()+1)/p, (ys.max()-ys.min()+1)/p, cx, cy, dc, iou))
    prev_mask = m; pc = (cx, cy)

print()
print('=== POGO: how much of the rider is buried? ===')
# rider silhouette: blue base ring + body. Use frame 44 (pre-impact) as the reference
# silhouette footprint, then measure how much of that footprint is covered by dark debris.
pp = PITCH['pogo']
f44 = load('pogo', 44)
cleanp = load('pogo', 0)
# rider footprint at landing = strong blue in later frames; find blue base ring
def blue_mask(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    return (b > 130) & (b - r > 40) & (b - g > 15)
for f in (44, 45, 47, 48, 50, 58, 62):
    a = load('pogo', f)
    r = win('pogo', a, 3.0)
    bm = blue_mask(r)
    print(' f%-3d t%+0.3f  blue base px %6d   (rider base visibility)' % (f, (f-45)/30.0, bm.sum()))

print()
print('=== hue separation of the two effects, dominant-mass histogram ===')
def hue_hist(shot, f, cells):
    a = load(shot, f); c = load(shot, 0)
    r = win(shot, a, cells); rc = win(shot, c, cells)
    m = (np.abs(r-rc).max(-1) > 26) & (lum(r) < 120)
    px = r[m]/255.0
    mx = px.max(1); mn = px.min(1); d = np.maximum(mx-mn, 1e-6)
    s = (mx-mn)/np.maximum(mx, 1e-4)
    rr, gg, bb = px[:, 0], px[:, 1], px[:, 2]
    h = np.zeros_like(mx)
    k = (mx == rr); h[k] = ((gg-bb)[k]/d[k]) % 6
    k = (mx == gg) & ~(mx == rr); h[k] = ((bb-rr)[k]/d[k])+2
    k = (mx == bb) & ~(mx == rr) & ~(mx == gg); h[k] = ((rr-gg)[k]/d[k])+4
    return h*60, s
hd, sd = hue_hist('death', 13, 3.2)
hp, sp = hue_hist('pogo', 48, 3.2)
for name, h, s in (('death f13', hd, sd), ('pogo  f48', hp, sp)):
    print('%s  hue p25/50/75 = %.0f/%.0f/%.0f   sat p50 %.3f p90 %.3f   frac sat>0.45: %.3f%%' % (
        name, np.percentile(h, 25), np.percentile(h, 50), np.percentile(h, 75),
        np.median(s), np.percentile(s, 90), 100*(s > 0.45).mean()))
