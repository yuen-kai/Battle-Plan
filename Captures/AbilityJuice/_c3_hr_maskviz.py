import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
import importlib.util
spec = importlib.util.spec_from_file_location('seg2', 'Captures/AbilityJuice/_c3_hr_seg2.py')

Y0, Y1, X0, X1 = 330, 700, 150, 950
P = {'r1': 'Captures/AbilityJuice/shots/r1/hitreact/f%04d.jpg',
     'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'}


def parts(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    green = (g - r > 34) & (g - b > 34)
    green = ndimage.binary_opening(green, np.ones((3, 3)))
    green = ndimage.binary_dilation(green, np.ones((5, 5)))
    solid = ((r - b) > -8) & ~green
    solid = ndimage.binary_closing(solid, np.ones((3, 3)))
    solid = ndimage.binary_opening(solid, np.ones((3, 3)))
    bridged = ndimage.binary_closing(solid, np.ones((41, 3)))
    lab, n = ndimage.label(bridged)
    blobs = []
    for i in range(1, n+1):
        m = (lab == i) & solid
        if m.sum() < 400:
            continue
        blobs.append(m)
    blobs.sort(key=lambda m: -m.sum())
    return blobs, green


tiles = []
for tag, fs in (('r2', [8, 12, 13, 15]), ('r1', [8, 13])):
    for f in fs:
        a = np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)[Y0:Y1, X0:X1]
        blobs, green = parts(a)
        vis = a.copy()
        if blobs:
            vis[blobs[0]] = vis[blobs[0]]*0.45 + np.array([255, 0, 160])*0.55
        for m in blobs[1:3]:
            vis[m] = vis[m]*0.45 + np.array([0, 255, 255])*0.55
        im = Image.fromarray(vis.clip(0, 255).astype(np.uint8))
        d = ImageDraw.Draw(im)
        d.text((6, 6), f'{tag} f{f} t={(f-12)/30.0:+.3f}  area={int(blobs[0].sum()) if blobs else 0}',
               fill=(255, 255, 0))
        tiles.append(im)

W, H = tiles[0].size
sheet = Image.new('RGB', (W, H*len(tiles)+4*(len(tiles)-1)), (10, 11, 15))
for i, t in enumerate(tiles):
    sheet.paste(t, (0, i*(H+4)))
sheet.save('Captures/AbilityJuice/_c3_hr_maskviz.png')
print(sheet.size)
