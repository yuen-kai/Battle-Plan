import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from _c4_wu_setup import load, t_of

# units are saturated pink/red; floor is desaturated blue-grey, plate is tan
def unit_mask(a):
    mx = a.max(2); mn = a.min(2)
    sat = (mx - mn) / np.maximum(mx, 1)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    pink = (r > 150) & (r - g > 60) & (r - b > 20) & (sat > 0.35)
    return pink


print('  i     t     blobs (cx, cy, area)')
tracks = {}
for i in list(range(11, 47)):
    a = load(i)
    m = unit_mask(a)
    m = ndimage.binary_closing(m, structure=np.ones((5, 5)))
    lab, n = ndimage.label(m)
    sz = ndimage.sum(m, lab, range(1, n + 1))
    blobs = []
    for j, s in enumerate(sz):
        if s > 500:
            cy, cx = ndimage.center_of_mass(m, lab, j + 1)
            blobs.append((cx, cy, int(s)))
    blobs.sort()
    tracks[i] = blobs
    print(f'{i:3d} {t_of(i):+.3f}  ' + '  '.join(f'({b[0]:6.1f},{b[1]:6.1f}) a={b[2]:5d}' for b in blobs))

CELL = 218.0
print('\n--- displacement of the LEFT (caster) blob ---')
for a, b in [(22, 40), (12, 45), (22, 45), (39, 45), (35, 45), (40, 45)]:
    if tracks[a] and tracks[b]:
        p, q = tracks[a][0], tracks[b][0]
        d = np.hypot(q[0] - p[0], q[1] - p[1])
        print(f'  f{a}(t={t_of(a):+.3f}) -> f{b}(t={t_of(b):+.3f}): {d:6.1f}px = {d/CELL:.2f} cells '
              f'(dx={q[0]-p[0]:+.1f}, dy={q[1]-p[1]:+.1f})')

print('\n--- per-frame speed of the left blob (px/frame) ---')
prev = None
for i in range(11, 47):
    if not tracks[i]:
        continue
    p = tracks[i][0]
    if prev is not None:
        v = np.hypot(p[0] - prev[0], p[1] - prev[1])
        print(f'{t_of(i):+.3f}  x={p[0]:6.1f} y={p[1]:6.1f}  v={v:5.1f}px/f  ({v/CELL:.3f} cells/f)')
    prev = p
