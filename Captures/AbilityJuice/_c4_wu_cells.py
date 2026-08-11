import numpy as np
from PIL import Image
from _c4_wu_setup import load, lum, amber, t_of

base = load(0)
ab = amber(base)
TH = 15.0
N = 70


def mask(i):
    return (amber(load(i)) - ab) > TH


m22, m40, m45 = mask(22), mask(40), mask(45)

for a, b, na, nb in [(m22, m40, 22, 40), (m22, m45, 22, 45), (m40, m45, 40, 45)]:
    print(f'IoU f{na}(t={t_of(na):+.3f}) vs f{nb}(t={t_of(nb):+.3f}) = '
          f'{(a & b).sum() / (a | b).sum():.4f}   covA={a.mean()*100:.2f}%  covB={b.mean()*100:.2f}%')

# --- resolve how many discrete cells exist in the full plate ---
# Use the difference between consecutive plateaus: each newly-lit region is one cell.
plateau_reps = [13, 17, 21, 24, 28, 31, 35, 39, 44]
prev = np.zeros_like(m22)
print('\nnewly-armed region per step (px area, centroid):')
cells = []
for k, i in enumerate(plateau_reps):
    m = mask(i)
    new = m & ~prev
    # clean tiny slivers
    from scipy import ndimage
    lab, n = ndimage.label(new)
    sizes = ndimage.sum(new, lab, range(1, n + 1))
    keep = np.zeros_like(new)
    big = 0
    for j, s in enumerate(sizes):
        if s > 2000:
            keep |= (lab == j + 1)
            big += 1
    ys, xs = np.nonzero(keep)
    print(f'  step{k+1} f{i} t={t_of(i):+.3f}  newarea={keep.sum():7d}px  blobs>{2000}px={big}  '
          f'centroid=({xs.mean():.0f},{ys.mean():.0f})' if keep.any() else f'  step{k+1} f{i} none')
    cells.append(keep)
    prev = m

print('\ntotal plate area f44 =', mask(44).sum(), 'px;  mean cell area =', mask(44).sum() / 9)
