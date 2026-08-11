"""HitStamp geometry + a literal scanline across the unit's edge (the round-2 complaint)."""
import numpy as np
from PIL import Image
from scipy import ndimage

P = {'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg',
     'r3': 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'}
PX_CELL = 287.0


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def frame(tag, f):
    return np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)


# ---------------- verify cell pitch from tile seams on the rest frame -------------
base = frame('r3', 8)
L = lum(base)
row = L[300, :]
seams = [x for x in range(2, 1022) if row[x] < row[x-2]-6 and row[x] < row[x+2]-6]
groups, cur = [], [seams[0]] if seams else []
for x in seams[1:]:
    if x - cur[-1] <= 4:
        cur.append(x)
    else:
        groups.append(int(np.mean(cur)))
        cur = [x]
if cur:
    groups.append(int(np.mean(cur)))
print('tile seams on row y=300:', groups)
if len(groups) > 1:
    print('seam spacing px:', np.diff(groups), ' (PX_CELL assumed', PX_CELL, ')')

# ---------------- literal scanline across the unit's left edge --------------------
print()
print('=' * 100)
print('SCANLINE across the unit edge — the round-2 "26 px soft ramp" complaint, re-measured')
print('=' * 100)


def scanline(tag, f, y, x0, x1, label):
    a = frame(tag, f)
    Lr = lum(a)[y, x0:x1]
    s = ''.join('#' if v < 60 else ('+' if v < 110 else ('.' if v < 165 else ' ')) for v in Lr)
    # first crossing from floor (>165) into unit (<90) and its width
    print(f'{label:26s} y={y} x{x0}-{x1}')
    print(f'   L: {s}')
    print(f'   min={Lr.min():5.1f} max={Lr.max():5.1f}   '
          f'n(L<60)={int((Lr<60).sum()):3d}  n(L<110)={int((Lr<110).sum()):3d}')
    # steepest 1px step
    dif = np.abs(np.diff(Lr))
    print(f'   steepest single-pixel step = {dif.max():.1f} L/px')


scanline('r3', 8, 492, 400, 660, 'r3 rest (pink unit)')
scanline('r2', 13, 505, 600, 860, 'r2 flash (the reject)')
scanline('r3', 12, 492, 560, 820, 'r3 t=0')
scanline('r3', 13, 495, 600, 860, 'r3 t=+33ms')

# ---------------- stamp isolation via difference from rest ------------------------
print()
print('=' * 100)
print('HITSTAMP — new dark matter, measured as a change from the rest frame')
print('=' * 100)
rest = frame('r3', 8)
Lrest = lum(rest)
for f in (12, 13, 14, 15, 16, 17, 18):
    a = frame('r3', f)
    La = lum(a)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    # new dark matter: dark now, was floor before
    newdark = (La < 110) & (Lrest > 150)
    newdark = ndimage.binary_opening(newdark, np.ones((3, 3)))
    lab, n = ndimage.label(ndimage.binary_closing(newdark, np.ones((9, 9))))
    if n == 0:
        print(f'f{f}: none')
        continue
    sizes = ndimage.sum(newdark, lab, range(1, n+1))
    i = int(np.argmax(sizes)) + 1
    m = newdark & (lab == i)
    ys, xs = np.where(m)
    Lm = La[m]
    dark60 = int((Lm < 60).sum())
    print(f'f{f} t={(f-12)/30.0:+.3f}  new-dark blob n={int(m.sum()):6d} '
          f'({m.sum()/PX_CELL**2:.3f} cell^2)  bbox {(xs.max()-xs.min()+1)/PX_CELL:.2f} x '
          f'{(ys.max()-ys.min()+1)/PX_CELL:.2f} cells  cx={xs.mean():6.1f} cy={ys.mean():6.1f}  '
          f'Lmin={Lm.min():4.1f} Lmed={np.median(Lm):5.1f}  n(L<60)={dark60}')

# ---------------- stamp vs unit overlap -------------------------------------------
print()
print('stamp centroid vs unit paint centroid (cells apart):')
for f in (12, 13, 14):
    a = frame('r3', f)
    La, r, g, b = lum(a), a[..., 0], a[..., 1], a[..., 2]
    green = (g - r > 34) & (g - b > 34)
    pm = ndimage.binary_opening((r-b > 50) & (g-b <= 25) & ~green, np.ones((3, 3)))
    lab, n = ndimage.label(ndimage.binary_closing(pm, np.ones((15, 15))))
    sizes = ndimage.sum(pm, lab, range(1, n+1))
    pm = pm & (lab == int(np.argmax(sizes))+1)
    body = ndimage.binary_fill_holes(ndimage.binary_closing(pm, np.ones((25, 25))))
    newdark = ndimage.binary_opening((La < 110) & (Lrest > 150), np.ones((3, 3)))
    lab2, n2 = ndimage.label(ndimage.binary_closing(newdark, np.ones((9, 9))))
    s2 = ndimage.sum(newdark, lab2, range(1, n2+1))
    sm = newdark & (lab2 == int(np.argmax(s2))+1)
    by, bx = np.where(body)
    sy, sx = np.where(sm)
    ov = (sm & body).sum()
    # also: how much of the stamp lands on the unit's silhouette (body OR its dark parts)
    unitish = ndimage.binary_dilation(body, np.ones((25, 25)))
    print(f'  f{f}: unit paint cx={bx.mean():6.1f}  stamp cx={sx.mean():6.1f}  '
          f'dx={(bx.mean()-sx.mean())/PX_CELL:+.2f} cells   '
          f'stamp∩body={int(ov)} px ({ov/max(sm.sum(),1)*100:.1f}% of stamp, '
          f'{ov/max(body.sum(),1)*100:.1f}% of body)   '
          f'stamp∩(body+12px)={int((sm & unitish).sum())} px')
