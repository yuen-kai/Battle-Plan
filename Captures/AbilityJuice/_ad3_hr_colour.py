"""Direct colour sampling: what colour is the unit, the stamp and the ember, really?"""
import numpy as np
from PIL import Image
from scipy import ndimage

P3 = 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'
P2 = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
PX_CELL = 287.0


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def fr(p, f):
    return np.asarray(Image.open(p % f).convert('RGB')).astype(np.float32)


def sat(a):
    mx, mn = a.max(2), a.min(2)
    return np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0)


def patch(p, f, y0, y1, x0, x1, label):
    a = fr(p, f)
    s = a[y0:y1, x0:x1].reshape(-1, 3)
    L = lum(a)[y0:y1, x0:x1]
    S = sat(a)[y0:y1, x0:x1]
    print(f'{label:34s} RGB=({s[:,0].mean():6.1f},{s[:,1].mean():6.1f},{s[:,2].mean():6.1f}) '
          f'L={L.mean():6.1f}  S={S.mean():5.3f}  R-B={s[:,0].mean()-s[:,2].mean():+6.1f} '
          f'G-B={s[:,1].mean()-s[:,2].mean():+6.1f}')


print('=' * 108)
print('COLOUR SAMPLES')
print('=' * 108)
patch(P3, 8, 470, 500, 505, 545, 'rest  unit body (pink)')
patch(P2, 13, 480, 510, 690, 730, 'r2 f13 unit body (bleached)')
patch(P3, 12, 470, 500, 660, 700, 'r3 f12 unit body')
patch(P3, 13, 475, 505, 705, 745, 'r3 f13 unit body')
patch(P3, 12, 430, 470, 520, 570, 'r3 f12 stamp interior (brown)')
patch(P3, 12, 465, 495, 455, 490, 'r3 f12 ember core')
patch(P3, 8, 250, 300, 100, 200, 'floor (rest)')
patch(P3, 12, 250, 300, 100, 200, 'floor (f12)')

# --------- exhaustive per-object census using hue bands, no connected components -----
print()
print('=' * 108)
print('OBJECT CENSUS by colour band, whole 1024x1024 frame')
print('  unitred : R-B>50 and G-B<=15      (crimson/pink team paint)')
print('  amber   : R-B>50 and G-B> 15      (ember + warm scorch)')
print('=' * 108)
hdr = (f"{'':>12} {'unitred':>8} {'/rest':>6} {'amber':>7} {'L<60':>7} {'/rest':>6} "
       f"{'L<110':>7} {'/rest':>6} {'L>250':>6} {'Lmax':>6} {'S>.45 & L>150':>14}")
print(hdr)
restA = fr(P3, 8)
r0 = dict()
for tag, p, frames in (('r2', P2, [8, 12, 13, 14]), ('r3', P3, [8, 12, 13, 14, 15, 16, 17, 20, 33])):
    for f in frames:
        a = fr(p, f)
        r, g, b = a[..., 0], a[..., 1], a[..., 2]
        L, S = lum(a), sat(a)
        green = (g - r > 34) & (g - b > 34)
        ur = int(((r-b > 50) & (g-b <= 15) & ~green).sum())
        am = int(((r-b > 50) & (g-b > 15) & ~green).sum())
        d60, d110 = int((L < 60).sum()), int((L < 110).sum())
        if f == 8 and not r0:
            r0 = dict(ur=ur, d60=d60, d110=d110)
        print(f"{tag+' f'+str(f):>12} {ur:8d} {ur/r0['ur']:6.2f} {am:7d} {d60:7d} "
              f"{d60/r0['d60']:6.2f} {d110:7d} {d110/r0['d110']:6.2f} {int((L>250).sum()):6d} "
              f"{L.max():6.1f} {int(((S>0.45)&(L>150)).sum()):14d}")
    print()
