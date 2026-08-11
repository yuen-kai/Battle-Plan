"""Isolate the unit from the HitStamp and the ember, then re-run the acceptance tests."""
import numpy as np
from PIL import Image
from scipy import ndimage

P = {'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg',
     'r3': 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'}
BOX = (380, 370, 880, 650)
PX_CELL = 287.0
REST = 8


def load(tag, f):
    a = np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)
    return a[BOX[1]:BOX[3], BOX[0]:BOX[2]]


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


# ---------------------------------------------------------------- hue census
print('=' * 100)
print('HUE CENSUS — what is actually red in each frame')
print('=' * 100)
for tag, frames in (('r2', [8]), ('r3', [8, 12, 13, 14, 15, 16])):
    for f in frames:
        a = load(tag, f)
        r, g, b = a[..., 0], a[..., 1], a[..., 2]
        L = lum(a)
        team = (r - b > 50)
        if not team.sum():
            continue
        gb = (g - b)[team]
        print(f'{tag} f{f:2d} t={(f-12)/30.0:+.3f}  n(R-B>50)={int(team.sum()):6d}  '
              f'G-B: p10={np.percentile(gb,10):+6.1f} med={np.median(gb):+6.1f} '
              f'p90={np.percentile(gb,90):+6.1f}   '
              f'n(G-B>25 "orange/amber")={int(((r-b>50)&(g-b>25)).sum()):6d}  '
              f'n(G-B<=25 "unit paint")={int(((r-b>50)&(g-b<=25)).sum()):6d}')

# --------------------------------------------------- unit / stamp separation
print()
print('=' * 100)
print('UNIT vs STAMP — separated by paint proximity, not by connected component')
print('=' * 100)


def separate(a):
    """Unit = the pink/red painted body + gun + its hugging outline.
    Stamp = dark matter that is not hugging the paint."""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    L = lum(a)
    green = (g - r > 34) & (g - b > 34)
    green = ndimage.binary_dilation(ndimage.binary_opening(green, np.ones((3, 3))), np.ones((7, 7)))
    # the unit's own paint: red-dominant AND not amber (the ember is amber)
    paint = (r - b > 50) & (g - b <= 25) & ~green
    paint = ndimage.binary_opening(paint, np.ones((3, 3)))
    lab, n = ndimage.label(ndimage.binary_closing(paint, np.ones((15, 15))))
    if n:
        sizes = ndimage.sum(paint, lab, range(1, n+1))
        paint = paint & (lab == (int(np.argmax(sizes)) + 1))
    body_core = ndimage.binary_closing(ndimage.binary_fill_holes(
        ndimage.binary_closing(paint, np.ones((21, 21)))), np.ones((9, 9)))
    near = ndimage.binary_dilation(body_core, np.ones((17, 17)))   # 8 px halo
    dark = (L < 60) & ~green
    return dict(L=L, paint=paint, body_core=body_core, near=near, dark=dark,
                unit_rim=dark & near, other_dark=dark & ~near, green=green,
                amber=(r - b > 50) & (g - b > 25) & ~green)


rest = separate(load('r2', REST))
rest_rim = int(rest['unit_rim'].sum())
rest_paint = int(rest['paint'].sum())
print(f'rest: unit-own rim (dark within 8px of paint) = {rest_rim}   unit paint = {rest_paint}')
print()
print(f"{'':>16} {'unit rim':>9} {'/rest':>6} | {'unit paint':>10} {'/rest':>6} | "
      f"{'amber':>7} | {'other dark':>10} {'cell^2':>7} | {'L<110 !rim':>10}")
for tag, frames in (('r2', [8, 12, 13]), ('r3', [8, 12, 13, 14, 15, 16, 17])):
    for f in frames:
        a = load(tag, f)
        s = separate(a)
        od = int(s['other_dark'].sum())
        soft = int(((s['L'] < 110) & ~s['near'] & ~s['green']).sum())
        print(f"{tag+' f'+str(f)+f' t={(f-12)/30.0:+.3f}':>16} {int(s['unit_rim'].sum()):9d} "
              f"{s['unit_rim'].sum()/rest_rim:6.2f} | {int(s['paint'].sum()):10d} "
              f"{s['paint'].sum()/rest_paint:6.2f} | {int(s['amber'].sum()):7d} | "
              f"{od:10d} {od/PX_CELL**2:7.3f} | {soft:10d}")
