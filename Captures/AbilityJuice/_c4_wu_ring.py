import numpy as np
from PIL import Image
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

base = load(0)
ab = amber(base)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]

H, W = Lb.shape
yy, xx = np.mgrid[0:H, 0:W]

# static geometry mask: exclude walls / crates (dark in baseline) and screen edges
floor = Lb > 120

CX, CY = 512.0, 545.0   # refined below


def ringpix(i):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    dL = L - Lb
    dA = amber(a) - ab
    # dark AND warm = engraved amber ring; excludes the flat plate tint (which is bright-ish warm)
    return (dL < -10) & (dA > 4) & floor


# --- refine centre using a mid frame with a big clean ring ---
m = ringpix(20)
lab, n = ndimage.label(m, structure=np.ones((3, 3)))
sz = ndimage.sum(m, lab, range(1, n + 1))
keep = np.isin(lab, [j + 1 for j, s in enumerate(sz) if s > 60])
ys, xs = np.nonzero(keep)
print('ring pixel count f20:', keep.sum())


def fit_circle(x, y):
    A = np.c_[2 * x, 2 * y, np.ones(len(x))]
    b = x ** 2 + y ** 2
    c = np.linalg.lstsq(A, b, rcond=None)[0]
    cx, cy = c[0], c[1]
    r = np.sqrt(c[2] + cx ** 2 + cy ** 2)
    return cx, cy, r


cx, cy, r = fit_circle(xs.astype(float), ys.astype(float))
print(f'fitted centre ({cx:.1f},{cy:.1f}) r={r:.1f}')
CX, CY = cx, cy

R = np.sqrt((xx - CX) ** 2 + (yy - CY) ** 2)

print('\n  i     t      npx   r_med  r_p10  r_p90  width  minL  dL@ring  arcCoverage%')
rows = []
for i in range(0, 50):
    m = ringpix(i)
    lab, n = ndimage.label(m, structure=np.ones((3, 3)))
    sz = ndimage.sum(m, lab, range(1, n + 1))
    keep = np.isin(lab, [j + 1 for j, s in enumerate(sz) if s > 60]) if n else m
    npx = keep.sum()
    if npx < 200:
        print(f'{i:3d} {t_of(i):+.3f}   {npx:5d}   --')
        rows.append((i, t_of(i), npx, np.nan, np.nan))
        continue
    rr = R[keep]
    rmed = np.median(rr)
    # radial thickness: keep only pixels within +-25px of median (the main annulus)
    sel = keep & (np.abs(R - rmed) < 30)
    rr2 = R[sel]
    width = np.percentile(rr2, 90) - np.percentile(rr2, 10)
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    dl = (L - Lb)[sel]
    # angular coverage
    th = np.arctan2((yy - CY)[sel], (xx - CX)[sel])
    hist, _ = np.histogram(th, bins=72, range=(-np.pi, np.pi))
    cov = (hist > 0).mean() * 100
    print(f'{i:3d} {t_of(i):+.3f}   {npx:5d}  {rmed:6.1f} {np.percentile(rr,10):6.1f} '
          f'{np.percentile(rr,90):6.1f}  {width:5.1f}  {L[sel].min():5.1f}  {dl.mean():+6.1f}   {cov:5.1f}')
    rows.append((i, t_of(i), npx, rmed, width))

np.save('_c4_wu_ring_rows.npy', np.array([(r[0], r[1], r[2], r[3], r[4]) for r in rows]))
