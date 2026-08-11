import numpy as np
from PIL import Image
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

base = load(0)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]

# --- camera stability: phase correlation on a static corner of the board ---
def patch(i, box):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    return L[box[1]:box[3], box[0]:box[2]]


def shift(p, q):
    P = np.fft.rfft2(p - p.mean())
    Q = np.fft.rfft2(q - q.mean())
    C = np.fft.irfft2(P * np.conj(Q) / (np.abs(P * np.conj(Q)) + 1e-9), s=p.shape)
    k = np.unravel_index(np.argmax(C), C.shape)
    dy = k[0] if k[0] < p.shape[0] / 2 else k[0] - p.shape[0]
    dx = k[1] if k[1] < p.shape[1] / 2 else k[1] - p.shape[1]
    return dx, dy


box = (700, 40, 1000, 340)   # top-right crates, away from the effect
print('camera drift (top-right board patch), px:')
for i in (12, 22, 30, 40, 45, 54):
    print(f'  t={t_of(i):+.3f}  {shift(patch(0, box), patch(i, box))}')

# --- plate colour ---
a = load(44)
ab = amber(base)
plate = ndimage.binary_opening((amber(a) - ab) > 15, structure=np.ones((15, 15)))
plate = ndimage.binary_erosion(plate, iterations=6)


def hsv_stats(rgb):
    mx = rgb.max(1); mn = rgb.min(1)
    sat = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1), 0)
    L = 0.2126 * rgb[:, 0] + 0.7152 * rgb[:, 1] + 0.0722 * rgb[:, 2]
    return sat, L


srgb = a[plate]
sat, L = hsv_stats(srgb)
print(f'\nOUR plate (f44): n={plate.sum()}  RGB median {np.median(srgb,0)}  '
      f'sat median {np.median(sat):.3f} p90 {np.percentile(sat,90):.3f}  L median {np.median(L):.0f}')
bl = base[plate]
bs, bL = hsv_stats(bl)
print(f'floor underneath: RGB median {np.median(bl,0)}  sat {np.median(bs):.3f}  L {np.median(bL):.0f}')
print(f'=> plate is {np.median(L)-np.median(bL):+.0f} luminance, {np.median(sat)-np.median(bs):+.3f} saturation vs floor')

# --- Clash Mini danger plates (left panel of the two clearest wind-ups) ---
for name, boxes in [('cm-everyability-22.jpg', [(120, 60, 250, 200)]),
                    ('cm-everyability-20.jpg', [(120, 130, 230, 215)])]:
    im = np.asarray(Image.open(f'strips/reference/{name}').convert('RGB')).astype(np.float32)
    for b in boxes:
        reg = im[b[1]:b[3], b[0]:b[2]].reshape(-1, 3)
        s, l = hsv_stats(reg)
        print(f'\nCM {name} plate patch {b}: RGB median {np.median(reg,0)} '
              f'sat median {np.median(s):.3f} p90 {np.percentile(s,90):.3f} L median {np.median(l):.0f}')
    # surrounding board for contrast
    b2 = (600, 200, 700, 300)
    reg2 = im[b2[1]:b2[3], b2[0]:b2[2]].reshape(-1, 3)
    s2, l2 = hsv_stats(reg2)
    print(f'   board nearby: RGB {np.median(reg2,0)} sat {np.median(s2):.3f} L {np.median(l2):.0f}'
          f'  => plate {np.median(l)-np.median(l2):+.0f} L, {np.median(s)-np.median(s2):+.3f} sat')
