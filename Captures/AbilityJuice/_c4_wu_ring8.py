import numpy as np
from scipy import ndimage
from _c4_wu_setup import load, t_of

CX, CY = 512.6, 549.0
base = load(0)
Lb0 = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]

NA, NR = 180, 420
th = (np.arange(NA) + 0.5) / NA * 2 * np.pi
rs = np.arange(8, NR)
SX = (CX + np.outer(np.cos(th), rs)).astype(np.int32).clip(0, 1023)
SY = (CY + np.outer(np.sin(th), rs)).astype(np.int32).clip(0, 1023)
BASE = Lb0[SY, SX]                     # polar baseline (walls/crates are dark here)
OK = BASE > 120                        # usable floor along each ray

print('  i     t   ring_r  cells  rays_hit  med_depth  med_fwhm  edge/px   dr')
prev = None
res = []
for i in range(11, 47):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    P = L[SY, SX]                      # (NA, NR-8) polar image
    bg = ndimage.uniform_filter1d(P, 31, axis=1, mode='nearest')
    d = P - bg
    d[~OK] = 0
    hits, depths, fw = [], [], []
    for k in range(NA):
        row = d[k]
        j = int(np.argmin(row))
        if row[j] < -14 and rs[j] > 25:
            # reject wide troughs (not a line)
            half = row[j] / 2
            lo = hi = j
            while lo > 0 and row[lo - 1] < half:
                lo -= 1
            while hi < len(row) - 1 and row[hi + 1] < half:
                hi += 1
            w = hi - lo + 1
            if w <= 24:
                hits.append(rs[j]); depths.append(row[j]); fw.append(w)
    if len(hits) < 12:
        print(f'{i:3d} {t_of(i):+.3f}   none ({len(hits)} rays)')
        res.append((t_of(i), np.nan)); prev = None; continue
    hits = np.array(hits)
    # modal radius: densest 12px window
    h, e = np.histogram(hits, bins=np.arange(20, NR, 6))
    kk = int(np.argmax(h))
    lo_, hi_ = e[kk] - 8, e[kk + 1] + 8
    sel = (hits >= lo_) & (hits <= hi_)
    r0 = np.median(hits[sel])
    dep = np.median(np.array(depths)[sel])
    w = np.median(np.array(fw)[sel])
    # edge steepness along a ray at the ring
    j0 = int(r0) - 8
    prof = np.median(P[:, max(0, j0 - 10):j0 + 11], axis=0)
    edge = np.max(np.abs(np.diff(prof))) if len(prof) > 2 else np.nan
    dr = '' if prev is None else f'{r0-prev:+6.1f}'
    print(f'{i:3d} {t_of(i):+.3f}  {r0:6.1f} {r0/218:5.2f}   {sel.sum():4d}/{NA}   '
          f'{dep:+6.1f}     {w:5.1f}   {edge:5.1f}  {dr}')
    prev = r0
    res.append((t_of(i), r0))

res = np.array(res)
np.save('_c4_wu_ring8.npy', res)
ok = np.isfinite(res[:, 1])
tt, rr = res[ok, 0], res[ok, 1]
r0 = rr[0]
print(f'\nstart r={r0:.0f}px at t={tt[0]:+.3f}')
for tq in (0.333, 0.60, 0.767, 0.90, 0.933, 1.00, 1.067, 1.10):
    k = np.argmin(np.abs(tt - tq))
    if abs(tt[k] - tq) < 0.02:
        print(f'  t={tq:+.3f}  r={rr[k]:6.1f}px  ({rr[k]/218:.2f} cells)  '
              f'travel done {(r0-rr[k])/r0*100:5.1f}%')
