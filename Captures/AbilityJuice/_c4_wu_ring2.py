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

# plate fill mask per frame
def plate(i, a=None):
    a = load(i) if a is None else a
    return (amber(a) - ab) > 15

# ring = thin dark ridge; find via difference-of-gaussian on dL restricted to floor
def dL(i):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    return L - Lb, a

# ---- estimate centre from the outside-plate arc at f13..f16 (ring big, plate = 1 cell)
d, a = dL(13)
p = plate(13, a)
cand = floor & ~ndimage.binary_dilation(p, iterations=6) & (d < -12)
lab, n = ndimage.label(cand, structure=np.ones((3, 3)))
sz = ndimage.sum(cand, lab, range(1, n + 1))
keep = np.isin(lab, [j + 1 for j, s in enumerate(sz) if s > 200])
ys, xs = np.nonzero(keep)
print('outside-plate dark px f13:', keep.sum())
A = np.c_[2 * xs, 2 * ys, np.ones(len(xs))].astype(float)
b = (xs.astype(float) ** 2 + ys.astype(float) ** 2)
c = np.linalg.lstsq(A, b, rcond=None)[0]
CX, CY = c[0], c[1]
Rfit = np.sqrt(c[2] + CX ** 2 + CY ** 2)
print(f'centre ({CX:.1f},{CY:.1f}) r={Rfit:.1f}')
R = np.sqrt((xx - CX) ** 2 + (yy - CY) ** 2)

print('\n  i     t    ring_r  trough_dL  fwhm_px  ang_cov%  ringL   note')
prev = None
for i in range(11, 47):
    d, a = dL(i)
    p = plate(i, a)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    # radial profile of dL relative to the LOCAL median at that radius (removes plate step)
    valid = floor & (R < 480)
    rb = R[valid].astype(int)
    dv = d[valid]
    prof = np.full(480, np.nan)
    for r0 in range(20, 480):
        s = dv[rb == r0]
        if len(s) > 40:
            prof[r0] = np.median(s)
    # detrend with a wide median filter so the plate's broad step is removed
    fin = np.isfinite(prof)
    sm = np.copy(prof)
    sm[fin] = ndimage.median_filter(prof[fin], size=41)
    resid = prof - sm
    if not np.isfinite(resid[20:470]).any():
        print(f'{i:3d} {t_of(i):+.3f}   none')
        continue
    rr = np.nanargmin(resid[:470])
    trough = resid[rr]
    # fwhm
    half = trough / 2
    lo = rr
    while lo > 21 and np.isfinite(resid[lo]) and resid[lo] < half:
        lo -= 1
    hi = rr
    while hi < 469 and np.isfinite(resid[hi]) and resid[hi] < half:
        hi += 1
    ann = valid & (np.abs(R - rr) <= 3)
    th = np.arctan2((yy - CY)[ann & (d < sm[rr] - 8)], (xx - CX)[ann & (d < sm[rr] - 8)])
    hist, _ = np.histogram(th, bins=72, range=(-np.pi, np.pi))
    cov = (hist > 0).mean() * 100
    ringL = np.median(L[ann & (d < sm[rr] - 8)]) if (ann & (d < sm[rr] - 8)).any() else np.nan
    note = ''
    if prev is not None:
        note = f'dr={rr-prev:+4d}'
    prev = rr
    print(f'{i:3d} {t_of(i):+.3f}   {rr:5d}   {trough:+7.1f}   {hi-lo:5d}   {cov:5.1f}   {ringL:5.1f}  {note}')
