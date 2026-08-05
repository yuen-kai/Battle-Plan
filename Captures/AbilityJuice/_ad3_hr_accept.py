"""Round-3 hit-reaction acceptance tests, run with the round-2 critic's own classifier."""
import numpy as np
from PIL import Image
from scipy import ndimage

P = {'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg',
     'r3': 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'}
BOX = (380, 370, 880, 650)          # identical to _c3_hr_mat.py
PX_CELL = 287.0
REST = 8                             # identical to _c3_hr_mat.py


def load(tag, f, box=BOX):
    a = np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)
    return a[box[1]:box[3], box[0]:box[2]]


def classify(a):
    """Verbatim from _c3_hr_mat.py — the classifier that produced 10427 / 10617."""
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    L = 0.2126*r + 0.7152*g + 0.0722*b
    mx, mn = a.max(2), a.min(2)
    S = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0)
    V = mx/255.0
    green = (g - r > 34) & (g - b > 34)
    green = ndimage.binary_dilation(ndimage.binary_opening(green, np.ones((3, 3))), np.ones((7, 7)))
    rim = (L < 60) & ~green
    body = (r - b > 50) & ~green
    gun = (r > g+3) & (g > b+3) & (L >= 60) & (L < 185) & (r - b <= 50) & ~green
    white = (L > 235) & (S < 0.18) & ~green
    unit = rim | body | gun | white
    unit = ndimage.binary_opening(unit, np.ones((3, 3)))
    unit = ndimage.binary_closing(unit, np.ones((5, 5)))
    lab, n = ndimage.label(ndimage.binary_closing(unit, np.ones((45, 5))))
    keep = np.zeros_like(unit)
    best = None
    for i in range(1, n+1):
        m = (lab == i) & unit
        if m.sum() > (best[0] if best else 400):
            best = (int(m.sum()), m)
    if best:
        keep = best[1]
    return dict(L=L, S=S, V=V, rim=rim & keep, body=body & keep, gun=gun & keep,
                white=white & keep, unit=keep, green=green, r=r, g=g, b=b)


print('=' * 96)
print('TEST A — the round-2 acceptance tests, run verbatim (component-level, as measured last round)')
print('=' * 96)
print(f"{'':>16} {'rim(L<60)':>10} {'/rest':>7} {'team(R-B>50)':>13} {'/rest':>7} "
      f"{'unit px':>8} {'clip L>250':>11} {'Lmax':>7}")

ref = {}
for tag, frames in (('r2', [REST, 12, 13]), ('r3', [REST, 12, 13, 14, 15, 16])):
    for f in frames:
        a = load(tag, f)
        c = classify(a)
        if tag == 'r2' and f == REST:
            ref = dict(rim=int(c['rim'].sum()), body=int(c['body'].sum()))
        rim, body = int(c['rim'].sum()), int(c['body'].sum())
        Lfull = 0.2126*np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)[..., 0] \
            + 0.7152*np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)[..., 1] \
            + 0.0722*np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)[..., 2]
        t = (f - 12)/30.0
        print(f"{tag+' f'+str(f)+f' t={t:+.3f}':>16} {rim:10d} {rim/max(ref['rim'],1):7.2f} "
              f"{body:13d} {body/max(ref['body'],1):7.2f} {int(c['unit'].sum()):8d} "
              f"{int((Lfull>250).sum()):11d} {Lfull.max():7.1f}")

print(f"\nround-2 rest reference reproduced: rim={ref['rim']}  team={ref['body']}   "
      f"(critic quoted 10427 / 10617)")
