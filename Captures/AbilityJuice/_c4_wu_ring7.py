import numpy as np
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

CX, CY = 512.6, 549.0
base = load(0)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]
yy, xx = np.mgrid[0:1024, 0:1024]
R = np.sqrt((xx - CX) ** 2 + (yy - CY) ** 2)
ANG = (((np.arctan2(yy - CY, xx - CX) + np.pi) / (2 * np.pi)) * 120).astype(np.int16) % 120

# mask out the target unit + its cast shadow, the caster, and the crates
unit = np.sqrt((xx - 509) ** 2 + (yy - 492) ** 2) < 78
unit |= np.sqrt((xx - 560) ** 2 + (yy - 560) ** 2) < 70   # its drop shadow
unit |= (xx < 300)                                        # caster side
usable = (Lb > 120) & ~unit

print('  i     t     r_px  cells  contrast  vs_local_bg  angcov%  fwhm  step/px')
prev = None
out = []
for i in range(11, 47):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    prof = np.full(430, np.nan)
    for r0 in range(20, 430):
        sel = (np.abs(R - r0) < 0.7) & usable
        if sel.sum() > 40:
            prof[r0] = np.percentile(L[sel], 10)
    v = np.isfinite(prof)
    filled = np.copy(prof)
    filled[~v] = np.nanmean(prof)
    sm = ndimage.uniform_filter1d(filled, 27)
    tr = prof - sm
    tr[:25] = 0
    tr[415:] = 0
    if not np.isfinite(tr).any():
        print(f'{i:3d} {t_of(i):+.3f}   none'); out.append((t_of(i), np.nan, np.nan)); continue
    r0 = int(np.nanargmin(tr))
    con = tr[r0]
    ann = (np.abs(R - r0) <= 3) & usable & (L < sm[r0] - max(6, -con * 0.4))
    cov = len(np.unique(ANG[ann])) / 120 * 100 if ann.any() else 0
    half = sm[r0] + con / 2
    lo = hi = r0
    while lo > 26 and np.isfinite(prof[lo - 1]) and prof[lo - 1] < half:
        lo -= 1
    while hi < 414 and np.isfinite(prof[hi + 1]) and prof[hi + 1] < half:
        hi += 1
    step = np.nanmax(np.abs(np.diff(prof[max(0, r0 - 8):r0 + 9])))
    print(f'{i:3d} {t_of(i):+.3f}  {r0:5d} {r0/218:5.2f}  L={prof[r0]:6.1f}  {con:+7.1f}   '
          f'{cov:5.1f}  {hi-lo+1:4d}  {step:5.1f}')
    out.append((t_of(i), r0, con))

out = np.array(out)
np.save('_c4_wu_ring7.npy', out)
ok = np.isfinite(out[:, 1]) & (out[:, 2] < -12)
tt, rr = out[ok, 0], out[ok, 1]
print('\nusable ring track:', ' '.join(f'{a:+.2f}:{int(b)}' for a, b in zip(tt, rr)))
if len(tt) > 2:
    r_start = rr[0]
    for tq in (0.60, 0.90, 1.00, 1.067):
        k = np.argmin(np.abs(tt - tq))
        print(f'  t={tt[k]:+.3f}: r={rr[k]:.0f}px ({rr[k]/218:.2f} cells)  '
              f'-> {(r_start-rr[k])/r_start*100:.0f}% of travel done')
