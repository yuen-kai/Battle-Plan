import numpy as np
from scipy import ndimage
from _c4_wu_setup import load, t_of

CX, CY = 512.6, 549.0
NA, NR = 240, 430
th = (np.arange(NA) + 0.5) / NA * 2 * np.pi
rs = np.arange(6, NR)
SX = (CX + np.outer(np.cos(th), rs)).astype(np.int32).clip(0, 1023)
SY = (CY + np.outer(np.sin(th), rs)).astype(np.int32).clip(0, 1023)


def polar(i):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    return L[SY, SX]


prev = polar(11)
print('frame-difference tracking: ring arrives where D<0')
print('  i     t    r_new  cells   depth   rays   |   r_old   dr_px  speed(px/f)')
track = []
for i in range(12, 48):
    cur = polar(i)
    D = cur - prev
    # arrival arc
    neg = np.where(D < -12, -D, 0)
    prof_n = neg.mean(0)
    pos = np.where(D > 12, D, 0)
    prof_p = pos.mean(0)
    if prof_n.max() < 0.35:
        print(f'{i:3d} {t_of(i):+.3f}   -- (no motion)')
        track.append((t_of(i), np.nan)); prev = cur; continue
    rn = rs[int(np.argmax(prof_n))]
    rp = rs[int(np.argmax(prof_p))]
    nrays = int(((D[:, max(0, rn - 6 - 6):rn - 6 + 7] < -12).any(1)).sum())
    dep = D[:, max(0, rn - 6 - 3):rn - 6 + 4].min()
    sp = (rp - rn)
    print(f'{i:3d} {t_of(i):+.3f}  {rn:5d} {rn/218:5.2f}  {dep:+7.1f}  {nrays:4d}/{NA}  |  '
          f'{rp:5d}   {sp:+5d}')
    track.append((t_of(i), rn))
    prev = cur

track = np.array(track)
np.save('_c4_wu_track9.npy', track)
ok = np.isfinite(track[:, 1])
tt, rr = track[ok, 0], track[ok, 1]
print('\nsmoothed shrink schedule (px radius of the arriving arc):')
print('   t      r     r/cells')
for t_, r_ in zip(tt, rr):
    print(f' {t_:+.3f} {r_:6.0f}   {r_/218:.2f}')
