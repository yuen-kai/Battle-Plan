import numpy as np
from PIL import Image
from scipy import ndimage

P = {'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg',
     'r3': 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'}


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


# global per-frame diff against frame 0 to find when anything happens at all
for tag in ('r2', 'r3'):
    base = np.asarray(Image.open(P[tag] % 0).convert('RGB')).astype(np.float32)
    Lb = lum(base)
    print(f'\n===== {tag} whole-frame activity =====')
    print(f"{'f':>3} {'nchg':>7} {'maxd':>6} {'Lmax':>6} {'nL>250':>7} {'ndark<60':>8} {'nRB>50':>7}")
    for f in range(61):
        a = np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)
        L = lum(a)
        d = np.abs(a - base).max(2)
        rb = a[..., 0] - a[..., 2]
        print(f'{f:3d} {int((d>25).sum()):7d} {d.max():6.1f} {L.max():6.1f} '
              f'{int((L>250).sum()):7d} {int((L<60).sum()):8d} {int((rb>50).sum()):7d}')
