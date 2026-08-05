"""Timeline + hot-core check for the r4 shockwave, and the same on CM impact panels."""
import numpy as np, glob, os
from PIL import Image, ImageFilter


def lum(a): return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


D = 'Captures/AbilityJuice/shots/r4/shockwave/'
ld = lambda i: np.asarray(Image.open(D + 'f%04d.jpg' % i).convert('RGB')).astype(np.float32)
n = len(glob.glob(D + 'f*.jpg'))
plate = np.median(np.stack([ld(i) for i in range(0, 11)]), axis=0)
Lp = lum(plate)
CELL = 218.0

print('=== r4 shockwave timeline (strip cuts f13 / f17 / f30) ===')
print(' frame   t       mass(cells2)  bright&sat  clip(any ch)  clip(ALL ch)  hotL>210  centroid drift')
prev = None
for i in range(10, 40):
    a = ld(i); La, Sa = lum(a), sat(a)
    fx = np.abs(a - plate).max(axis=2) > 18
    mass = fx & (La < Lp - 25)
    bs = fx & (La > 200) & (Sa > 0.45)
    clipany = fx & (a.max(axis=2) >= 250)
    clipall = fx & (a.min(axis=2) >= 250)
    hot = fx & (La > 210)
    if mass.sum() > 200:
        ys, xs = np.where(mass); cen = (xs.mean(), ys.mean())
    else:
        cen = None
    drift = '' if (cen is None or prev is None) else '%.3f cells' % (np.hypot(cen[0] - prev[0], cen[1] - prev[1]) / CELL)
    prev = cen if cen else prev
    mark = ' <<< STRIP' if i in (13, 17, 30) else ''
    print('  f%-4d %+.3fs   %6.2f       %6d       %6d        %6d      %6d   %s%s'
          % (i, (i - 12) / 30.0, mass.sum() / CELL ** 2, bs.sum(), clipany.sum(), clipall.sum(), hot.sum(), drift, mark))

print()
print('=== CENTRE OF THE BLAST: is there a hot core, or is the middle dark? ===')
for i in (13, 17):
    a = ld(i); La = lum(a)
    fx = np.abs(a - plate).max(axis=2) > 18
    ys, xs = np.where(fx)
    cx, cy = int(xs.mean()), int(ys.mean())
    r = 60
    win = La[cy - r:cy + r, cx - r:cx + r]
    print('  r4 f%d  central %dpx disc: L mean %.0f  max %.0f  (plate %.0f)  -> %s'
          % (i, 2 * r, win.mean(), win.max(), Lp[cy - r:cy + r, cx - r:cx + r].mean(),
             'DARK CENTRE' if win.mean() < Lp[cy, cx] else 'lit centre'))

print()
print('=== CM references: brightest pixels & clipping in their impact panel ===')
for p in sorted(glob.glob('Captures/AbilityJuice/strips/reference/*.jpg'))[:17]:
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    h, w, _ = a.shape; pw = w // 3
    mid = a[:, pw:2 * pw]; L = lum(mid)
    ca = (mid.min(axis=2) >= 250)
    print('  %-34s panel2 Lmax %3.0f  L>240 %6d px (%.2f%%)  white-clipped %5d px (%.2f%%)'
          % (os.path.basename(p), L.max(), (L > 240).sum(), 100.0 * (L > 240).mean(),
             ca.sum(), 100.0 * ca.mean()))

print()
print('=== OURS r4 strip: same query ===')
a = np.asarray(Image.open('Captures/AbilityJuice/strips/r4/shockwave.jpg').convert('RGB')).astype(np.float32)
h, w, _ = a.shape; pw = w // 3
for i in range(3):
    mid = a[:, i * pw:(i + 1) * pw]; L = lum(mid)
    ca = (mid.min(axis=2) >= 250)
    print('  panel %d  Lmax %3.0f  L>240 %6d px (%.2f%%)  white-clipped %5d px (%.2f%%)'
          % (i + 1, L.max(), (L > 240).sum(), 100.0 * (L > 240).mean(), ca.sum(), 100.0 * ca.mean()))
