import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

P = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
PX_CELL = 287.0


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


base = np.asarray(Image.open(P % 8).convert('RGB')).astype(np.float32)
for f in (12, 13, 14, 15):
    a = np.asarray(Image.open(P % f).convert('RGB')).astype(np.float32)
    d = np.abs(a - base).max(2)
    m = d > 25
    m = ndimage.binary_opening(m, np.ones((3, 3)))
    lab, n = ndimage.label(ndimage.binary_closing(m, np.ones((9, 9))))
    sizes = ndimage.sum(m, lab, range(1, n+1))
    order = np.argsort(sizes)[::-1][:4]
    print(f'\nframe {f} (t={(f-12)/30.0:+.3f}s) — changed regions vs rest:')
    for i in order:
        mm = (lab == i+1) & m
        if mm.sum() < 500:
            continue
        ys, xs = np.where(mm)
        L = lum(a)[mm]
        S = ((a.max(2)-a.min(2))/np.maximum(a.max(2), 1e-6))[mm]
        print(f'  blob n={int(mm.sum()):6d} ({mm.sum()/PX_CELL**2:.3f} cell^2)  '
              f'cx={xs.mean():6.1f} cy={ys.mean():6.1f}  '
              f'x[{xs.min()}-{xs.max()}] y[{ys.min()}-{ys.max()}]  '
              f'w={(xs.max()-xs.min()+1)/PX_CELL:.2f}cell h={(ys.max()-ys.min()+1)/PX_CELL:.2f}cell  '
              f'L med={np.median(L):.0f} S med={np.median(S):.2f}')

# distance between the two blobs at the strip frame
a = np.asarray(Image.open(P % 13).convert('RGB')).astype(np.float32)
d = np.abs(a - base).max(2) > 25
d = ndimage.binary_opening(d, np.ones((3, 3)))
lab, n = ndimage.label(ndimage.binary_closing(d, np.ones((9, 9))))
sizes = ndimage.sum(d, lab, range(1, n+1))
order = np.argsort(sizes)[::-1][:2]
bs = []
for i in order:
    mm = (lab == i+1) & d
    ys, xs = np.where(mm)
    bs.append((xs.min(), xs.max(), xs.mean()))
bs.sort()
print(f'\nstrip frame f13: gap of clean floor between the two masses = '
      f'{(bs[1][0]-bs[0][1])/PX_CELL:.2f} cells ({bs[1][0]-bs[0][1]} px); '
      f'centre-to-centre {(bs[1][2]-bs[0][2])/PX_CELL:.2f} cells')

# zoomed strip panel 2
im = Image.open('Captures/AbilityJuice/strips/r2/hitreact.jpg').convert('RGB')
p = im.height
gut = (im.width - 3*p)//2
panel = im.crop((p+gut, 0, p+gut+p, p)).resize((760, 760), Image.LANCZOS)
panel.save('Captures/AbilityJuice/_c3_hr_panel2.png')
print('wrote panel2', panel.size)
