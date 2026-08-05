"""Stamp measured alone: cut the fused dark mass at the unit's own left edge."""
import numpy as np
from PIL import Image
from scipy import ndimage

P3 = 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'
PX_CELL = 287.0


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


for f in (12, 13, 14, 15, 16):
    a = np.asarray(Image.open(P3 % f).convert('RGB')).astype(np.float32)
    L, r, g, b = lum(a), a[..., 0], a[..., 1], a[..., 2]
    green = (g - r > 34) & (g - b > 34)
    paint = ndimage.binary_opening((r-b > 50) & (g-b <= 25) & ~green, np.ones((3, 3)))
    lab, n = ndimage.label(ndimage.binary_closing(paint, np.ones((15, 15))))
    s = ndimage.sum(paint, lab, range(1, n+1))
    paint = paint & (lab == int(np.argmax(s))+1)
    pxs = np.where(paint)[1]
    cut = int(np.percentile(pxs, 2)) - 6          # just left of the unit's own paint
    win = np.zeros(L.shape, bool)
    win[380:640, 300:cut] = True                  # stamp side only, unit excluded
    dark110 = (L < 110) & win
    dark110 = ndimage.binary_opening(dark110, np.ones((3, 3)))
    ys, xs = np.where(dark110)
    if not len(ys):
        print(f'f{f}: nothing')
        continue
    area = dark110.sum()
    print(f'f{f} t={(f-12)/30.0:+.3f}  cut at x={cut} (unit paint starts here)')
    print(f'    stamp-only sub-L110 area {int(area):6d} px = {area/PX_CELL**2:.3f} cell^2  '
          f'-> equivalent diameter {2*np.sqrt(area/PX_CELL**2/np.pi):.2f} cells')
    print(f'    bbox {(xs.max()-xs.min()+1)/PX_CELL:.2f} x {(ys.max()-ys.min()+1)/PX_CELL:.2f} cells'
          f'   Lmin={L[dark110].min():.1f}  Lmed={np.median(L[dark110]):.1f}  '
          f'n(L<60)={int((L[dark110]<60).sum())}')
