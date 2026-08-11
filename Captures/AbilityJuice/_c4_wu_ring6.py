import numpy as np
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

base = load(0)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]
H, W = Lb.shape
yy, xx = np.mgrid[0:H, 0:W]
floor = Lb > 120

CX, CY = 512.6, 549.0
R = np.sqrt((xx - CX) ** 2 + (yy - CY) ** 2)
Ri = R.astype(int)
ANG = (((np.arctan2(yy - CY, xx - CX) + np.pi) / (2 * np.pi)) * 120).astype(np.int16) % 120

MAXR = 470
valid = floor & (Ri < MAXR)
ri_v = Ri[valid]
ang_v = ANG[valid]
cnt = np.bincount(ri_v, minlength=MAXR).astype(float)


def radprof(L):
    s = np.bincount(ri_v, weights=L[valid], minlength=MAXR)
    return np.where(cnt > 30, s / np.maximum(cnt, 1), np.nan)


print('  i     t    ring_r  cells  trough  angcov%  ringL  thickness_px  edge_L/px')
rows = []
for i in range(11, 50):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    # use a low percentile per radius so a partial arc still shows
    p20 = np.full(MAXR, np.nan)
    order = np.argsort(ri_v)
    Lv = L[valid][order]
    rv = ri_v[order]
    idx = np.searchsorted(rv, np.arange(MAXR + 1))
    for r0 in range(MAXR):
        seg = Lv[idx[r0]:idx[r0 + 1]]
        if len(seg) > 30:
            p20[r0] = np.percentile(seg, 15)
    sh = ndimage.uniform_filter1d(np.nan_to_num(p20, nan=np.nanmean(p20)), 25)
    trough = p20 - sh
    trough[:45] = 0
    trough[440:] = 0
    r0 = int(np.nanargmin(trough))
    tv = trough[r0]
    ann = valid & (np.abs(R - r0) <= 3) & (L < sh[r0] - 20)
    cov = len(np.unique(ANG[ann])) / 120 * 100 if ann.any() else 0
    # thickness: contiguous radii where p20 < shoulder - half the trough
    half = sh[r0] + tv / 2
    lo = r0
    while lo > 46 and p20[lo - 1] < half:
        lo -= 1
    hi = r0
    while hi < 439 and p20[hi + 1] < half:
        hi += 1
    edge = np.nanmax(np.abs(np.diff(p20[max(0, r0 - 12):r0 + 13])))
    good = tv < -14 and cov > 18
    tag = '' if good else '   <weak>'
    print(f'{i:3d} {t_of(i):+.3f}   {r0:5d}  {r0/218:5.2f}  {tv:+7.1f}  {cov:5.1f}  '
          f'{np.median(L[ann]) if ann.any() else float("nan"):6.1f}  {hi-lo+1:5d}  {edge:6.1f}{tag}')
    rows.append((t_of(i), r0 if good else np.nan, tv, cov))

rows = np.array(rows)
np.save('_c4_wu_radius6.npy', rows)
ok = ~np.isnan(rows[:, 1])
tt, rr = rows[ok, 0], rows[ok, 1]
print(f'\ntracked from t={tt[0]:+.3f} (r={rr[0]:.0f}px) to t={tt[-1]:+.3f} (r={rr[-1]:.0f}px)')
for tq in (0.333, 0.60, 0.90, 0.933, 1.00, 1.067, 1.10):
    k = np.argmin(np.abs(tt - tq))
    if abs(tt[k] - tq) < 0.02:
        print(f'   t={tq:+.3f}  r={rr[k]:6.0f}px = {rr[k]/218:.2f} cells')
