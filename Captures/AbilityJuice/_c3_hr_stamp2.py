import numpy as np
from PIL import Image
from scipy import ndimage

P = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
PX_CELL = 287.0
base = np.asarray(Image.open(P % 8).convert('RGB')).astype(np.float32)


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


for f in (12, 13, 14):
    a = np.asarray(Image.open(P % f).convert('RGB')).astype(np.float32)
    m = ndimage.binary_opening(np.abs(a - base).max(2) > 25, np.ones((3, 3)))
    lab, n = ndimage.label(m)
    sizes = ndimage.sum(m, lab, range(1, n+1))
    keep = [(int(sizes[i]), i+1) for i in np.argsort(sizes)[::-1][:6] if sizes[i] > 800]
    print(f'\nframe {f} t={(f-12)/30.0:+.3f}s')
    info = []
    for s, i in keep:
        mm = lab == i
        ys, xs = np.where(mm)
        L, A = lum(a)[mm], a[mm]
        S = ((A.max(1)-A.min(1))/np.maximum(A.max(1), 1e-6))
        info.append((xs.mean(), xs.min(), xs.max(), s, np.median(L), np.median(S), ys.min(), ys.max()))
    info.sort()
    for cx, x0, x1, s, L, S, y0, y1 in info:
        print(f'  x[{x0:4d}-{x1:4d}] cx={cx:6.1f}  n={s:6d} ({s/PX_CELL**2:.3f} cell^2) '
              f'w={(x1-x0+1)/PX_CELL:.2f}c h={(y1-y0+1)/PX_CELL:.2f}c  Lmed={L:5.0f} Smed={S:.2f}')
    if len(info) >= 2:
        left, right = info[0], info[-1]
        print(f'  --> gap between leftmost and rightmost mass: '
              f'{(right[1]-left[2])/PX_CELL:+.2f} cells ({right[1]-left[2]:+d} px)')
