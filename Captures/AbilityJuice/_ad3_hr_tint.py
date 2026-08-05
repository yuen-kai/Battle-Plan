"""Damage-tint decay, and the squint test."""
import numpy as np
from PIL import Image
from scipy import ndimage

P3 = 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


print('=' * 96)
print('DAMAGE TINT DECAY — mean colour of the unit body (paint pixels only)')
print('=' * 96)
print(f"{'f':>4} {'t':>7} {'R':>6} {'G':>6} {'B':>6} {'L':>6} {'S':>6} {'G-B':>6} {'n':>6}")
for f in [8, 12, 13, 14, 16, 17, 18, 20, 24, 28, 33, 38, 42, 45, 50, 54, 58]:
    a = np.asarray(Image.open(P3 % f).convert('RGB')).astype(np.float32)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    green = (g - r > 34) & (g - b > 34)
    m = ndimage.binary_opening((r-b > 60) & ~green & (lum(a) > 40), np.ones((5, 5)))
    lab, n = ndimage.label(ndimage.binary_closing(m, np.ones((15, 15))))
    if n:
        s = ndimage.sum(m, lab, range(1, n+1))
        m = m & (lab == int(np.argmax(s))+1)
    if m.sum() < 200:
        continue
    px = a[m]
    mx, mn = px.max(1), px.min(1)
    S = ((mx-mn)/np.maximum(mx, 1e-6)).mean()
    print(f'{f:4d} {(f-12)/30.0:+7.3f} {px[:,0].mean():6.1f} {px[:,1].mean():6.1f} '
          f'{px[:,2].mean():6.1f} {lum(a)[m].mean():6.1f} {S:6.3f} '
          f'{px[:,1].mean()-px[:,2].mean():+6.1f} {int(m.sum()):6d}')
