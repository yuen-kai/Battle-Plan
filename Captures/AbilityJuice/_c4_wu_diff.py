import numpy as np
from PIL import Image
from _c4_wu_setup import load, amber, t_of

base = load(0)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]

tiles = []
for i in [12, 13, 17, 22, 28, 34, 39, 42, 45]:
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    d = L - Lb
    img = np.zeros((1024, 1024, 3), np.uint8)
    img[..., 0] = np.clip(-d * 6, 0, 255)      # darker -> red
    img[..., 1] = np.clip(d * 6, 0, 255)       # brighter -> green
    img[..., 2] = np.clip(np.abs(amber(a) - amber(base)) * 2, 0, 255)
    tiles.append(np.asarray(Image.fromarray(img).resize((340, 340))))
row = lambda ts: np.hstack(ts)
grid = np.vstack([row(tiles[0:3]), row(tiles[3:6]), row(tiles[6:9])])
Image.fromarray(grid).save('_c4_wu_diff.png')
print('saved', grid.shape)

# sample the ring in f13 along a horizontal scan through the ring's left arc
a = load(13)
L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
for yrow in (300, 400, 500):
    seg = L[yrow, 150:420]
    segb = Lb[yrow, 150:420]
    d = seg - segb
    j = int(np.argmin(d))
    print(f'row {yrow}: min dL={d[j]:.1f} at x={150+j}, L={seg[j]:.0f} (base {segb[j]:.0f})')
    print('   dL:', ' '.join(f'{v:+.0f}' for v in d[max(0,j-12):j+13]))
