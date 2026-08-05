"""Zoom crops of the r4 shockwave mass vs r3 vs a Clash Mini dust mass, plus a squint pass."""
import numpy as np
from PIL import Image, ImageFilter


def lum(a): return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def bbox_of_effect(rd, f):
    D = 'Captures/AbilityJuice/shots/%s/shockwave/' % rd
    ld = lambda i: np.asarray(Image.open(D + 'f%04d.jpg' % i).convert('RGB')).astype(np.float32)
    plate = np.median(np.stack([ld(i) for i in range(0, 11)]), axis=0)
    a = ld(f)
    fx = np.abs(a - plate).max(axis=2) > 18
    ys, xs = np.where(fx)
    return a, (xs.min(), ys.min(), xs.max(), ys.max())


tiles = []
for rd, f in (('r3', 17), ('r4', 17)):
    a, (x0, y0, x1, y1) = bbox_of_effect(rd, f)
    cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    half = max(x1 - x0, y1 - y0) // 2 + 20
    crop = Image.fromarray(a.astype(np.uint8)).crop((cx - half, cy - half, cx + half, cy + half))
    tiles.append(crop.resize((560, 560), Image.LANCZOS))
Image.fromarray(np.concatenate([np.asarray(t) for t in tiles], axis=1)).save(
    'Captures/AbilityJuice/_ad4_sw_zoom.png')

# tight crop on one arc of r4 so the material read is unambiguous
a, (x0, y0, x1, y1) = bbox_of_effect('r4', 17)
cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
half = max(x1 - x0, y1 - y0) // 2 + 20
lo = Image.fromarray(a.astype(np.uint8)).crop((cx - half, cy - half, cx, cy + half // 2))
lo.resize((lo.width * 3, lo.height * 3), Image.LANCZOS).save('Captures/AbilityJuice/_ad4_sw_arc.png')

# squint: r4 strip vs the strongest CM strips, both crushed to thumbnail then blown back up
outs = []
for p in ['Captures/AbilityJuice/strips/r4/shockwave.jpg',
          'Captures/AbilityJuice/strips/reference/cm-clashabilities-04.jpg',
          'Captures/AbilityJuice/strips/reference/cm-8newabilities-07.jpg']:
    im = Image.open(p).convert('RGB').resize((388, 128), Image.LANCZOS)
    outs.append(np.asarray(im.resize((776, 256), Image.NEAREST)))
Image.fromarray(np.concatenate(outs, axis=0)).save('Captures/AbilityJuice/_ad4_sw_squint.png')

# greyscale test: does the effect lose anything when colour is thrown away? (r3's decisive test)
for tag, p in (('r4', 'Captures/AbilityJuice/strips/r4/shockwave.jpg'),):
    im = Image.open(p).convert('RGB')
    g = im.convert('L').convert('RGB')
    Image.fromarray(np.concatenate([np.asarray(im), np.asarray(g)], axis=0)).resize(
        (776, 512), Image.LANCZOS).save('Captures/AbilityJuice/_ad4_sw_grey.png')
print('written')
