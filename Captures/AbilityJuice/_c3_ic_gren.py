import numpy as np
from PIL import Image, ImageDraw

PG = 'Captures/AbilityJuice/shots/r3/grenade/'
W = np.array([0.2126, 0.7152, 0.0722], np.float32)
PX = 218.0


def load(i):
    return np.asarray(Image.open(PG + 'f%04d.jpg' % i)).astype(np.float32)


def sat(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    return np.where(mx > 1, (mx - mn) / np.maximum(mx, 1), 0.0)


print('grenade: where does the white impact core appear in a real match?')
print('%-4s %-8s %9s %9s %9s' % ('f', 't(s)', 'whiteC', 'clip%', 'whitePx'))
best = []
for i in range(38, 62):
    f = load(i)
    S = sat(f)
    mn = f.min(axis=2)
    wh = (mn >= 235) & (S < 0.10)
    clip = (f[:, :, 0] >= 250) & (f[:, :, 1] >= 250) & (f[:, :, 2] >= 250)
    wD = 2 * np.sqrt(wh.sum() / np.pi) / PX
    print('%-4d %+8.4f %9.3f %9.2f %9d' % (i, (i - 47) / 30.0, wD, 100 * clip.mean(), wh.sum()))
    best.append((wh.sum(), i))
best.sort(reverse=True)
print('peak white frame:', best[0])

pk = best[0][1]
sheet = Image.new('RGB', (300 * 6, 322), (10, 10, 10))
d = ImageDraw.Draw(sheet)
for k, i in enumerate([pk - 2, pk - 1, pk, pk + 1, pk + 2, 47]):
    im = Image.open(PG + 'f%04d.jpg' % i).convert('RGB').resize((296, 296), Image.LANCZOS)
    sheet.paste(im, (k * 300 + 2, 24))
    d.text((k * 300 + 6, 6), 'f%d  t%+.3fs%s' % (i, (i - 47) / 30.0, '  <-STRIP' if i == 47 else ''),
           fill=(255, 255, 0))
sheet.save('Captures/AbilityJuice/_c3_ic_grenade.png')
print('wrote _c3_ic_grenade.png')
