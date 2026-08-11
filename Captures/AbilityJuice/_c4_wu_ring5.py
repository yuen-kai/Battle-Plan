import numpy as np
from PIL import Image
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

base = load(0)
ab = amber(base)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]
H, W = Lb.shape
yy, xx = np.mgrid[0:H, 0:W]
floor = Lb > 120           # exclude crates + their baked shadows

CX, CY = 512.6, 549.0
R = np.sqrt((xx - CX) ** 2 + (yy - CY) ** 2)
TH = np.arctan2(yy - CY, xx - CX)
THB = ((TH + np.pi) / (2 * np.pi) * 180).astype(np.int16) % 180   # 2-degree bins

# --- what luminance is the plate tint vs the ring? ---
a = load(44)
L44 = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
plate = ndimage.binary_opening((amber(a) - ab) > 15, structure=np.ones((13, 13)))
print('plate tint L: p10 %.0f  p50 %.0f  p90 %.0f' % tuple(np.percentile(L44[plate], [10, 50, 90])))
print('clean floor L: p10 %.0f  p50 %.0f  p90 %.0f' % tuple(np.percentile(Lb[floor], [10, 50, 90])))

DARK = 95.0


def hough(i):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    m = floor & (L < DARK) & (Lb > 120)
    rad = R[m].astype(int)
    ang = THB[m]
    best = (0, -1, 0)
    scores = np.zeros(500)
    for r0 in range(8, 460):
        s = (np.abs(rad - r0) <= 3)
        if s.sum() < 100:
            continue
        cov = len(np.unique(ang[s])) / 180.0
        scores[r0] = cov
    r0 = int(np.argmax(scores))
    return r0, scores[r0], L, m


print('\n  i     t    ring_r  cells  angcov  |  edge profile across ring (dL per px)')
prev = None
rows = []
for i in range(11, 48):
    r0, cov, L, m = hough(i)
    if cov < 0.15:
        print(f'{i:3d} {t_of(i):+.3f}   none  (best cov {cov:.2f})')
        rows.append((t_of(i), np.nan)); continue
    ring = m & (np.abs(R - r0) <= 4)
    # radial luminance profile through the ring, averaged over angle
    prof = []
    for dr in range(-14, 15):
        sel = floor & (np.abs(R - (r0 + dr)) < 0.7)
        prof.append(np.percentile(L[sel], 20) if sel.sum() > 200 else np.nan)
    prof = np.array(prof)
    edge = np.nanmax(np.abs(np.diff(prof)))
    d = '' if prev is None else f'{r0-prev:+5d}'
    print(f'{i:3d} {t_of(i):+.3f}   {r0:5d}  {r0/218:5.2f}   {cov:5.2f}  {d}  '
          f'minL={np.nanmin(prof):5.1f} steepest={edge:5.1f}/px  npx={ring.sum()}')
    prev = r0
    rows.append((t_of(i), r0))

rows = np.array(rows)
np.save('_c4_wu_radius5.npy', rows)
ok = ~np.isnan(rows[:, 1])
tt, rr = rows[ok, 0], rows[ok, 1]
print(f'\nstart r {rr[0]:.0f}px at t={tt[0]:+.3f}')
i9 = np.argmin(np.abs(tt - 0.90))
i11 = np.argmin(np.abs(tt - 1.10))
print(f'r at t=0.900 -> {rr[i9]:.0f}px ; r at t=1.100 -> {rr[i11]:.0f}px')
print(f'travel completed by t=0.900: {(rr[0]-rr[i9])/rr[0]*100:.0f}%')
print(f'travel in final 200ms      : {(rr[i9]-rr[i11])/rr[0]*100:.0f}%')
