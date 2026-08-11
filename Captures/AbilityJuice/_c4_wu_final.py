import numpy as np
from PIL import Image
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

base = load(0)
ab = amber(base)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]

# --- 1. is the caster doing ANYTHING in panel 1? ---
box = (0, 400, 320, 620)
def cc(i):
    a = load(i)
    return a[box[1]:box[3], box[0]:box[2]]
for i in (0, 6, 12, 22, 30, 34, 40):
    d = np.abs(cc(i) - cc(0)).mean()
    print(f'caster crop f{i:02d} t={t_of(i):+.3f}: mean abs diff vs pre-windup f0 = {d:.2f}')

# --- 2. chevron contrast on the plate ---
a = load(44)
L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
plate = ndimage.binary_opening((amber(a) - ab) > 15, structure=np.ones((15, 15)))
core = ndimage.binary_erosion(plate, iterations=10)
Lp = L[core]
lo = np.percentile(Lp, 5)
tint = np.percentile(Lp, 70)
chev = core & (L < tint - 18)
chev = ndimage.binary_opening(chev, structure=np.ones((4, 4)))
lab, n = ndimage.label(chev)
sz = ndimage.sum(chev, lab, range(1, n + 1))
big = [s for s in sz if s > 700]
print(f'\nplate tint L (p70) = {tint:.0f};  chevron marks: {len(big)} of area>700px, '
      f'total {int(sum(big))}px = {sum(big)/plate.sum()*100:.1f}% of plate')
if chev.any():
    cl = np.median(L[chev])
    print(f'chevron L median {cl:.0f} vs plate {tint:.0f} -> contrast {cl-tint:+.0f} '
          f'(Weber {abs(cl-tint)/tint:.2f}); board tile-to-tile noise = 18')

# --- 3. cell-snap: how much of the plate boundary is straight? ---
bnd = plate & ~ndimage.binary_erosion(plate)
ys, xs = np.nonzero(bnd)
print(f'\nplate boundary pixels: {bnd.sum()}')
# straightness: for each boundary pixel, fit a line to its 15px neighbourhood
pts = np.c_[xs, ys].astype(float)
tree_ok = 0
from scipy.spatial import cKDTree
tree = cKDTree(pts)
samp = pts[::7]
for p in samp:
    idx = tree.query_ball_point(p, 11)
    q = pts[idx] - p
    if len(q) < 6:
        continue
    u, s, vt = np.linalg.svd(q - q.mean(0), full_matrices=False)
    if s[1] / max(s[0], 1e-6) < 0.10:
        tree_ok += 1
print(f'boundary pixels lying on a locally straight edge (aspect<0.10): '
      f'{tree_ok/len(samp)*100:.1f}%')

# --- 4. what fraction of panel-1 pixels are "the effect"? ---
for i, lbl in [(22, 'panel1 t=0.333'), (40, 'panel2 t=0.933')]:
    m = ndimage.binary_opening((amber(load(i)) - ab) > 15, structure=np.ones((13, 13)))
    ys, xs = np.nonzero(m)
    print(f'\n{lbl}: plate {m.mean()*100:.2f}% of frame, bbox '
          f'{(xs.max()-xs.min())/218:.2f} x {(ys.max()-ys.min())/218:.2f} cells')

# --- 5. peak saturated+bright pixels (Clash Mini carries 1-5%) ---
for i, lbl in [(22, 'p1'), (40, 'p2'), (46, 'fire+33ms')]:
    aa = load(i)
    LL = 0.2126 * aa[..., 0] + 0.7152 * aa[..., 1] + 0.0722 * aa[..., 2]
    mx, mn = aa.max(2), aa.min(2)
    sat = (mx - mn) / np.maximum(mx, 1)
    print(f'{lbl}: pixels bright(L>200) AND saturated(>0.45) = {np.mean((LL>200)&(sat>0.45))*100:.3f}%'
          f' ; clipped(all ch >=250) = {np.mean(aa.min(2)>=250)*100:.3f}%')
