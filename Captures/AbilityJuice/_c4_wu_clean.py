import numpy as np
from PIL import Image
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

base = load(0)
ab = amber(base)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]
H, W = Lb.shape
yy, xx = np.mgrid[0:H, 0:W]
floor = Lb > 120

DISK = np.zeros((15, 15), bool)
gy, gx = np.mgrid[-7:8, -7:8]
DISK[gy ** 2 + gx ** 2 <= 49] = True


def prep(i):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    return a, L, L - Lb, amber(a) - ab


def plate_only(i):
    """amber region with thin (<15px) structures removed -> the cell tint alone"""
    a, L, d, dA = prep(i)
    m = dA > 15
    return ndimage.binary_opening(m, structure=DISK)


def ring_only(i):
    """thin dark ridge = d well below its local background"""
    a, L, d, dA = prep(i)
    bg = ndimage.median_filter(d, size=35)
    r = (d < bg - 25) & (d < -45) & floor
    r = ndimage.binary_opening(r, structure=np.ones((2, 2)))
    lab, n = ndimage.label(r, structure=np.ones((3, 3)))
    if n:
        sz = ndimage.sum(r, lab, range(1, n + 1))
        r = np.isin(lab, [j + 1 for j, s in enumerate(sz) if s > 120])
    return r, L, d


print('== plate (thin structures removed) ==')
print('  i     t     cov%   cells(est)')
cov = {}
for i in list(range(11, 47)):
    m = plate_only(i)
    cov[i] = m.mean() * 100
    print(f'{i:3d} {t_of(i):+.3f}  {cov[i]:6.2f}')

p22, p40, p45 = plate_only(22), plate_only(40), plate_only(45)
print(f'\nIoU(plate-only) f22 vs f40 = {(p22&p40).sum()/(p22|p40).sum():.4f}')
print(f'IoU(plate-only) f40 vs f45 = {(p40&p45).sum()/(p40|p45).sum():.4f}')
