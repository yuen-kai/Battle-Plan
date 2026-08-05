import numpy as np
from _c4_wu_setup import load, lum, amber, t_of, N

base = load(0)
ab = amber(base)
TH = 15.0

masks = {}
print(' i     t     cov%    bbox(w,h)      cx      cy   IoU(prev)')
prev = None
for i in range(N):
    a = load(i)
    m = (amber(a) - ab) > TH
    masks[i] = m
    cov = m.mean() * 100
    ys, xs = np.nonzero(m)
    if len(xs) == 0:
        print(f'{i:3d} {t_of(i):+.3f}  {cov:6.2f}   -')
        prev = m
        continue
    bw, bh = xs.max() - xs.min() + 1, ys.max() - ys.min() + 1
    iou = ''
    if prev is not None and prev.any():
        iou = f'{(m & prev).sum() / (m | prev).sum():.3f}'
    print(f'{i:3d} {t_of(i):+.3f}  {cov:6.2f}   {bw:4d}x{bh:4d}  {xs.mean():6.1f} {ys.mean():6.1f}  {iou}')
    prev = m

np.save('_c4_wu_masks.npy', np.stack([masks[i] for i in range(N)]))
print()
for pair in [(22, 40), (22, 44), (40, 44)]:
    a, b = masks[pair[0]], masks[pair[1]]
    inter = (a & b).sum()
    uni = (a | b).sum()
    print(f'IoU f{pair[0]} vs f{pair[1]} = {inter/uni:.4f}   '
          f'areaA={a.sum()} areaB={b.sum()}  A\\B={ (a&~b).sum() }  B\\A={ (b&~a).sum() }')
