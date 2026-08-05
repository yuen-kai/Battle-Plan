"""Enumerate every changed mass at the impact frames, and render the masks so they can be eyeballed."""
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

P3 = 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'
PX_CELL = 287.0


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def fr(f):
    return np.asarray(Image.open(P3 % f).convert('RGB')).astype(np.float32)


rest = fr(8)
Lrest = lum(rest)

print('=' * 108)
print('ALL new-dark masses (L<110 now, floor >150 at rest), largest first')
print('=' * 108)
for f in (12, 13, 14, 15, 16):
    a = fr(f)
    La = lum(a)
    newdark = ndimage.binary_opening((La < 110) & (Lrest > 150), np.ones((3, 3)))
    lab, n = ndimage.label(newdark)
    sizes = ndimage.sum(newdark, lab, range(1, n+1))
    order = np.argsort(sizes)[::-1][:6]
    print(f'\nf{f} t={(f-12)/30.0:+.3f}  total new-dark px = {int(newdark.sum())} '
          f'({newdark.sum()/PX_CELL**2:.3f} cell^2)')
    for i in order:
        if sizes[i] < 400:
            continue
        m = newdark & (lab == i+1)
        ys, xs = np.where(m)
        Lm = La[m]
        print(f'   n={int(m.sum()):6d} ({m.sum()/PX_CELL**2:.3f} c^2) cx={xs.mean():6.1f} '
              f'cy={ys.mean():6.1f}  bbox {(xs.max()-xs.min()+1)/PX_CELL:.2f}x'
              f'{(ys.max()-ys.min()+1)/PX_CELL:.2f} cells  '
              f'Lmin={Lm.min():5.1f} Lmed={np.median(Lm):5.1f} Lmean={Lm.mean():5.1f}')

# ------------------------------------------------- mask render
BOX = (380, 360, 900, 660)
SC = 2
frames = [12, 13, 14]
out = Image.new('RGB', ((BOX[2]-BOX[0])*SC*len(frames), (BOX[3]-BOX[1])*SC*2 + 40), (10, 10, 10))
d = ImageDraw.Draw(out)
for ci, f in enumerate(frames):
    a = fr(f)
    La, r, g, b = lum(a), a[..., 0], a[..., 1], a[..., 2]
    green = (g - r > 34) & (g - b > 34)
    paint = ndimage.binary_opening((r-b > 50) & (g-b <= 25) & ~green, np.ones((3, 3)))
    lab, n = ndimage.label(ndimage.binary_closing(paint, np.ones((15, 15))))
    s = ndimage.sum(paint, lab, range(1, n+1))
    paint = paint & (lab == int(np.argmax(s))+1)
    amber = (r-b > 50) & (g-b > 25) & ~green
    dark = La < 60
    mid = (La >= 60) & (La < 110)
    viz = np.zeros(a.shape, np.uint8)
    viz[..., 2] = np.where(dark, 255, 0)                       # blue   = L<60
    viz[..., 1] = np.where(mid, 200, 0)                        # green  = 60-110
    viz[..., 0] = np.where(paint, 255, 0)                      # red    = unit paint
    viz[amber] = (255, 200, 0)                                 # yellow = amber/ember
    viz[green] = (255, 255, 255)                               # white  = health bar
    im = Image.fromarray(viz[BOX[1]:BOX[3], BOX[0]:BOX[2]]).resize(
        ((BOX[2]-BOX[0])*SC, (BOX[3]-BOX[1])*SC), Image.NEAREST)
    raw = Image.open(P3 % f).convert('RGB').crop(BOX).resize(
        ((BOX[2]-BOX[0])*SC, (BOX[3]-BOX[1])*SC), Image.LANCZOS)
    out.paste(raw, (ci*(BOX[2]-BOX[0])*SC, 20))
    out.paste(im, (ci*(BOX[2]-BOX[0])*SC, 20 + (BOX[3]-BOX[1])*SC + 20))
    d.text((ci*(BOX[2]-BOX[0])*SC + 6, 4), f'f{f} t={(f-12)/30.0:+.3f}s', fill=(255, 255, 0))
out.save('Captures/AbilityJuice/_ad3_hr_masks.png')
print('\nwrote masks', out.size)
