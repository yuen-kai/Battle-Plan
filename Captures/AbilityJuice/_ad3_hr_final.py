"""Final numbers: precise edge ramp, and the HitStamp isolated without a rest-frame gate."""
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


print('=' * 108)
print('EDGE RAMP — walk in from clean floor to the unit body, print every pixel')
print('=' * 108)


def ramp(p, f, y, xstart, xend, label):
    """xstart on clean floor, walking toward the body."""
    a = fr(p, f)
    L = lum(a)
    step = 1 if xend > xstart else -1
    vals, xs = [], []
    for x in range(xstart, xend, step):
        vals.append(L[y, x])
        xs.append(x)
    vals = np.array(vals)
    # ramp = pixels from the last floor-value pixel (>165) to the first body pixel (<90)
    fl = np.where(vals > 165)[0]
    bd = np.where(vals < 90)[0]
    width = None
    if len(fl) and len(bd):
        b0 = bd[bd > fl[0]]
        if len(b0):
            f0 = fl[fl < b0[0]].max()
            width = b0[0] - f0
    print(f'\n{label}   row y={y}, walking x {xstart}->{xend}')
    print('   L: ' + ' '.join(f'{v:3.0f}' for v in vals))
    print(f'   floor->body ramp width = {width} px    '
          f'steepest step = {np.abs(np.diff(vals)).max():.0f} L/px    min L = {vals.min():.0f}')


ramp(P3, 8, 492, 440, 480, 'REST (pink unit, left edge)')
ramp(P2, 13, 505, 620, 665, 'R2 FLASH t=+33ms (the reject, left edge)')
ramp(P3, 12, 492, 612, 655, 'R3 t=0 (right edge, away from stamp)')
ramp(P3, 13, 495, 800, 755, 'R3 t=+33ms (right edge, walking left)')

print()
print('=' * 108)
print('HITSTAMP — all L<110 masses, no rest-frame gate; identify the one that is not the unit')
print('=' * 108)
rest = fr(P3, 8)
Lrest = lum(rest)
restdark = ndimage.binary_opening(Lrest < 110, np.ones((3, 3)))
lab0, n0 = ndimage.label(restdark)
s0 = ndimage.sum(restdark, lab0, range(1, n0+1))
# the rest unit's own dark mass, for reference
ys0, xs0 = np.where(restdark & (lab0 == int(np.argsort(s0)[::-1][0])+1))
for f in (12, 13, 14, 15, 16):
    a = fr(P3, f)
    La = lum(a)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    green = (g - r > 34) & (g - b > 34)
    paint = ndimage.binary_opening((r-b > 50) & (g-b <= 25) & ~green, np.ones((3, 3)))
    labp, npn = ndimage.label(ndimage.binary_closing(paint, np.ones((15, 15))))
    sp = ndimage.sum(paint, labp, range(1, npn+1))
    paint = paint & (labp == int(np.argmax(sp))+1)
    pys, pxs = np.where(paint)
    pcx, pcy = pxs.mean(), pys.mean()
    dark = ndimage.binary_opening(La < 110, np.ones((3, 3)))
    lab, n = ndimage.label(dark)
    sizes = ndimage.sum(dark, lab, range(1, n+1))
    print(f'\nf{f} t={(f-12)/30.0:+.3f}   unit paint centroid ({pcx:.0f},{pcy:.0f})')
    for i in np.argsort(sizes)[::-1][:5]:
        if sizes[i] < 1200:
            continue
        m = dark & (lab == i+1)
        ys, xs = np.where(m)
        if ys.min() < 120 or ys.max() > 900:
            kind = 'board obstacle'
        elif abs(xs.mean()-pcx) < 90:
            kind = 'UNIT (+ any stamp fused to it)'
        else:
            kind = '<<< HITSTAMP >>>'
        Lm = La[m]
        print(f'   n={int(m.sum()):6d} ({m.sum()/PX_CELL**2:.3f} c^2) cx={xs.mean():6.1f} '
              f'cy={ys.mean():6.1f} bbox {(xs.max()-xs.min()+1)/PX_CELL:.2f}x'
              f'{(ys.max()-ys.min()+1)/PX_CELL:.2f}c  Lmin={Lm.min():5.1f} '
              f'Lmed={np.median(Lm):5.1f}  {kind}')

print()
print('=' * 108)
print('SEAM OCCLUSION TEST — floor tile seam contrast under the stamp vs clean floor')
print('=' * 108)
# clean-floor seam contrast: find a vertical seam in the rest frame
row = Lrest[620, :]
dips = [(x, row[x]) for x in range(5, 1019)
        if row[x] < row[x-4]-5 and row[x] < row[x+4]-5]
if dips:
    x0 = dips[len(dips)//2][0]
    print(f'clean seam at x={x0} on row 620: local max {max(row[x0-8:x0+9]):.1f} '
          f'min {min(row[x0-8:x0+9]):.1f}  contrast {max(row[x0-8:x0+9])-min(row[x0-8:x0+9]):.1f}')
for f in (12, 13):
    La = lum(fr(P3, f))
    # look for the seam inside the stamp footprint
    sub = La[560:600, 470:560]
    print(f'f{f}: under stamp lower lobe rows 560-600 x470-560 -> '
          f'L mean {sub.mean():.1f} min {sub.min():.1f} max {sub.max():.1f} '
          f'range {sub.max()-sub.min():.1f}  (rest same window: '
          f'mean {Lrest[560:600,470:560].mean():.1f} range '
          f'{Lrest[560:600,470:560].max()-Lrest[560:600,470:560].min():.1f})')
