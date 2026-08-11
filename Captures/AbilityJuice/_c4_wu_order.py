import numpy as np
from PIL import Image
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

base = load(0)
ab = amber(base)
TH = 15.0


def mask(i):
    return (amber(load(i)) - ab) > TH


# first-lit frame index per pixel, using only fully-settled plateau frames
plateaus = [13, 17, 21, 24, 28, 31, 35, 39, 44, 45]
first = np.full(base.shape[:2], 99, np.int16)
for k, i in enumerate(plateaus):
    m = mask(i)
    first[(first == 99) & m] = k

# colour map the arming order
pal = np.array([
    [255, 60, 60], [255, 150, 40], [250, 235, 60], [130, 230, 70], [60, 220, 190],
    [70, 150, 255], [160, 90, 255], [255, 90, 200], [255, 255, 255], [120, 120, 120]
], np.uint8)
out = (load(44) * 0.35).astype(np.uint8)
for k in range(10):
    sel = first == k
    if sel.sum() > 500:
        out[sel] = pal[k]
Image.fromarray(out).save('_c4_wu_order.png')

print('pixels first lit at each step:')
for k in range(10):
    n = (first == k).sum()
    if n:
        ys, xs = np.nonzero(first == k)
        print(f'  step{k} (f{plateaus[k]} t={t_of(plateaus[k]):+.3f}) area={n:7d} '
              f'centroid=({xs.mean():.0f},{ys.mean():.0f})')

# how many distinct grid cells? erode the full plate and label
full = mask(44)
lab, n = ndimage.label(full)
sizes = ndimage.sum(full, lab, range(1, n + 1))
print('\nfull plate components:', [int(s) for s in sorted(sizes, reverse=True)[:6]])
print('full plate area', full.sum())

# count cells: chevrons are dark marks inside each cell -> count them
a44 = load(44)
L = 0.2126 * a44[..., 0] + 0.7152 * a44[..., 1] + 0.0722 * a44[..., 2]
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]
dark = full & (L < np.percentile(L[full], 12))
lab2, n2 = ndimage.label(dark)
s2 = ndimage.sum(dark, lab2, range(1, n2 + 1))
big = [(int(s), ndimage.center_of_mass(dark, lab2, j + 1)) for j, s in enumerate(s2) if s > 400]
print(f'\ndark marks inside plate (>400px): {len(big)}')
for s, c in sorted(big, key=lambda z: -z[0]):
    print(f'   area={s:6d} at ({c[1]:.0f},{c[0]:.0f})')
