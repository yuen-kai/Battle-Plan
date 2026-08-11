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


def prep(i):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    return a, L, L - Lb, (amber(a) - ab)


a, L, d, dA = prep(13)
p = dA > 15
cand = floor & ~ndimage.binary_dilation(p, iterations=8) & (d < -35)
print('cand px', cand.sum())
lab, n = ndimage.label(cand, structure=np.ones((3, 3)))
sz = ndimage.sum(cand, lab, range(1, n + 1))
print('n comps', n, 'top sizes', sorted(sz, reverse=True)[:6])
keep = np.isin(lab, [j + 1 for j, s in enumerate(sz) if s > 150])
ys, xs = np.nonzero(keep)
print('kept', keep.sum())
A = np.c_[2 * xs, 2 * ys, np.ones(len(xs))].astype(float)
bb = (xs.astype(float) ** 2 + ys.astype(float) ** 2)
c = np.linalg.lstsq(A, bb, rcond=None)[0]
CX, CY = c[0], c[1]
Rfit = np.sqrt(c[2] + CX ** 2 + CY ** 2)
resid = np.sqrt((xs - CX) ** 2 + (ys - CY) ** 2) - Rfit
print(f'centre ({CX:.1f},{CY:.1f}) r={Rfit:.1f} residual sd={resid.std():.2f}')
R = np.sqrt((xx - CX) ** 2 + (yy - CY) ** 2)

# ring hue
sel = keep
rgb = a[sel]
mx, mn = rgb.max(1), rgb.min(1)
sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0)
print(f'ring RGB median {np.median(rgb,0)}  L median {np.median(L[sel]):.1f}  sat median {np.median(sat):.3f}')
brgb = base[sel]
print(f'floor under ring RGB median {np.median(brgb,0)}  L {np.median(Lb[sel]):.1f}')

print('\n  i     t     ring_r  Ncand   ringL  dL     ang_cov%   dr/frame  travel%')
rs = {}
for i in range(11, 48):
    a, L, d, dA = prep(i)
    p = dA > 15
    # ring is much darker than the plate tint; use an absolute-dark criterion
    cand = floor & (d < -35)
    lab, n = ndimage.label(cand, structure=np.ones((3, 3)))
    if n == 0:
        print(f'{i:3d} {t_of(i):+.3f}  none'); continue
    sz = ndimage.sum(cand, lab, range(1, n + 1))
    keep = np.isin(lab, [j + 1 for j, s in enumerate(sz) if s > 150])
    if keep.sum() < 300:
        print(f'{i:3d} {t_of(i):+.3f}  none ({keep.sum()})'); rs[i] = np.nan; continue
    # ring = the annulus; take the modal radius via histogram
    rr = R[keep]
    hist, edges = np.histogram(rr, bins=np.arange(0, 520, 4))
    k = int(np.argmax(hist))
    rmode = (edges[k] + edges[k + 1]) / 2
    ann = keep & (np.abs(R - rmode) < 14)
    th = np.arctan2((yy - CY)[ann], (xx - CX)[ann])
    hcov, _ = np.histogram(th, bins=72, range=(-np.pi, np.pi))
    cov = (hcov > 0).mean() * 100
    rs[i] = np.median(R[ann])
    dr = rs[i] - rs.get(i - 1, np.nan)
    print(f'{i:3d} {t_of(i):+.3f}   {rs[i]:6.1f} {keep.sum():7d}  {np.median(L[ann]):5.1f} '
          f'{np.median(d[ann]):+6.1f}   {cov:5.1f}      {dr:+6.1f}')

np.save('_c4_wu_r.npy', np.array([[i, rs.get(i, np.nan)] for i in range(11, 48)]))
