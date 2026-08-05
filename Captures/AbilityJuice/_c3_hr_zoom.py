import numpy as np
from PIL import Image

D = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
D1 = 'Captures/AbilityJuice/shots/r1/hitreact/f%04d.jpg'

# full frame of rest, to locate the unit
a = np.asarray(Image.open(D % 8).convert('RGB')).astype(np.float32)
r, g, b = a[..., 0], a[..., 1], a[..., 2]
redness = r - b
ys, xs = np.where(redness > 40)
print('red blob bbox y', ys.min(), ys.max(), 'x', xs.min(), xs.max(), 'n', len(ys))
print('centroid', ys.mean(), xs.mean())

# green health bar
greenish = (g - r > 30) & (g - b > 30)
ys2, xs2 = np.where(greenish)
print('green bar bbox y', ys2.min(), ys2.max(), 'x', xs2.min(), xs2.max(), 'n', len(ys2))

CY, CX, H = 500, 512, 220
box = (CX-H, CY-H, CX+H, CY+H)
for tag, path in (('r2', D), ('r1', D1)):
    tiles = []
    for f in (8, 12, 13, 14, 15, 26):
        im = Image.open(path % f).convert('RGB').crop(box).resize((330, 330), Image.NEAREST)
        tiles.append(im)
    sheet = Image.new('RGB', (330*len(tiles)+4*(len(tiles)-1), 330), (10, 11, 15))
    for i, t in enumerate(tiles):
        sheet.paste(t, (i*334, 0))
    sheet.save(f'Captures/AbilityJuice/_c3_hr_zoom_{tag}.png')
    print('wrote', tag)
