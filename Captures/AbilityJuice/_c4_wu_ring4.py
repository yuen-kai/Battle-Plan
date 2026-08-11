import numpy as np
from PIL import Image
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

base = load(0)
ab = amber(base)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]
H, W = Lb.shape
yy, xx = np.mgrid[0:H, 0:W]
floor = Lb > 120


def ring_mask(i):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    d = L - Lb
    bg = ndimage.median_filter(d, size=41)
    r = (d < bg - 22) & floor
    lab, n = ndimage.label(r, structure=np.ones((3, 3)))
    if n:
        sz = ndimage.sum(r, lab, range(1, n + 1))
        r = np.isin(lab, [j + 1 for j, s in enumerate(sz) if s > 300])
    return r, a, L, d


def fit(xs, ys):
    A = np.c_[2 * xs, 2 * ys, np.ones(len(xs))].astype(float)
    b = xs.astype(float) ** 2 + ys.astype(float) ** 2
    c = np.linalg.lstsq(A, b, rcond=None)[0]
    cx, cy = c[0], c[1]
    return cx, cy, np.sqrt(c[2] + cx ** 2 + cy ** 2)


# centre from an early, large, clean ring (f14) excluding the caster/unit region
r, a, L, d = ring_mask(14)
ys, xs = np.nonzero(r)
cx, cy, r0 = fit(xs, ys)
# iterate: reject outliers
for _ in range(4):
    rad = np.sqrt((xs - cx) ** 2 + (ys - cy) ** 2)
    k = np.abs(rad - np.median(rad)) < 40
    cx, cy, r0 = fit(xs[k], ys[k])
    xs, ys = xs[k], ys[k]
print(f'ring centre ({cx:.1f},{cy:.1f})  r={r0:.1f}  n={len(xs)}  '
      f'radial sd={np.sqrt((xs-cx)**2+(ys-cy)**2).std():.2f}px')
R = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2)

print('\n  i     t     r_px   r_cells  npx  thick  ringL  dL_local  ang_cov%   dr')
prevr = None
res = []
for i in range(11, 48):
    r, a, L, d = ring_mask(i)
    if r.sum() < 400:
        print(f'{i:3d} {t_of(i):+.3f}   -- ({r.sum()})')
        res.append((t_of(i), np.nan)); prevr = None; continue
    rad = R[r]
    hist, edges = np.histogram(rad, bins=np.arange(0, 560, 3))
    k = int(np.argmax(hist))
    rm = (edges[k] + edges[k + 1]) / 2
    ann = r & (np.abs(R - rm) < 18)
    rr = R[ann]
    thick = np.percentile(rr, 95) - np.percentile(rr, 5)
    th = np.arctan2((yy - cy)[ann], (xx - cx)[ann])
    hc, _ = np.histogram(th, bins=72, range=(-np.pi, np.pi))
    cov = (hc > 0).mean() * 100
    med = np.median(rr)
    dr = '' if prevr is None else f'{med-prevr:+6.1f}'
    print(f'{i:3d} {t_of(i):+.3f}  {med:6.1f}   {med/218:5.2f}  {ann.sum():6d}  {thick:5.1f} '
          f' {np.median(L[ann]):5.1f}   {np.median(d[ann]):+6.1f}   {cov:5.1f}   {dr}')
    prevr = med
    res.append((t_of(i), med))

res = np.array(res)
np.save('_c4_wu_radius.npy', res)

# travel analysis
ok = ~np.isnan(res[:, 1])
tt, rr = res[ok, 0], res[ok, 1]
r_start = rr[0]
print(f'\nring r at first frame {tt[0]:+.3f}s = {r_start:.0f}px')
for tcut in (0.9, 1.0):
    m = tt <= tcut
    if m.any():
        print(f'  r at t<={tcut}: last = {rr[m][-1]:.0f}px  -> travelled {(r_start-rr[m][-1])/r_start*100:.0f}% of start radius')
last200 = (tt > 0.90) & (tt <= 1.10)
if last200.any():
    print(f'  frames in last 200ms before fire: {last200.sum()}, r from {rr[last200][0]:.0f} to {rr[last200][-1]:.0f}')
