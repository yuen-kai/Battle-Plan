import numpy as np
from scipy import ndimage
from _c4_wu_setup import load, t_of

CX, CY = 512.6, 549.0
NA = 360
th = (np.arange(NA) + 0.5) / NA * 2 * np.pi
rs = np.arange(4, 430)
SX = (CX + np.outer(np.cos(th), rs)).astype(np.int32).clip(0, 1023)
SY = (CY + np.outer(np.sin(th), rs)).astype(np.int32).clip(0, 1023)

SCHED = {12: 350, 15: 347, 18: 339, 21: 314, 22: 306, 24: 288, 26: 268, 28: 244,
         30: 218, 32: 190, 34: 159, 36: 126, 38: 91, 39: 73, 40: 54, 41: 38, 42: 23, 43: 20}

print('  i     t     r    ringL   bgL   contrast  Weber  edge_L/px  width_px  angcov%')
for i, r0 in SCHED.items():
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    P = L[SY, SX]
    j = r0 - 4
    win = P[:, max(0, j - 16):j + 17]
    # per-ray: ring value = min in window, bg = median of the outer thirds
    ringv = win.min(1)
    bg = np.median(np.c_[win[:, :8], win[:, -8:]], axis=1)
    con = ringv - bg
    hit = con < -12
    prof = np.median(win, axis=0)
    edge = np.max(np.abs(np.diff(prof)))
    half = np.median(bg) + np.median(con[hit]) / 2 if hit.any() else np.nan
    w = int((prof < half).sum()) if np.isfinite(half) else -1
    print(f'{i:3d} {t_of(i):+.3f} {r0:5d}  {np.median(ringv[hit]) if hit.any() else float("nan"):6.1f} '
          f'{np.median(bg):6.1f}  {np.median(con[hit]) if hit.any() else float("nan"):+7.1f}  '
          f'{abs(np.median(con[hit])/np.median(bg)) if hit.any() else float("nan"):5.2f}   '
          f'{edge:6.1f}     {w:4d}     {hit.mean()*100:5.1f}')
