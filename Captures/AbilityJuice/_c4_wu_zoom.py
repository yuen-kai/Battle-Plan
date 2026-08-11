import numpy as np
from PIL import Image, ImageDraw
from _c4_wu_setup import load, t_of

CX, CY = 513, 549
S = 250  # half-size of crop


def crop(i, boost=False):
    im = Image.open(f'shots/r3/windup/f{i:04d}.jpg').convert('RGB')
    c = im.crop((CX - S, CY - S, CX + S, CY + S)).resize((300, 300), Image.LANCZOS)
    d = ImageDraw.Draw(c)
    d.rectangle([0, 0, 299, 16], fill=(0, 0, 0))
    d.text((4, 4), f'{t_of(i):+.3f}s', fill=(255, 255, 0))
    return np.asarray(c)


idxs = [[24, 27, 30, 33, 36], [38, 40, 41, 42, 43], [44, 45, 46, 47, 48]]
grid = np.vstack([np.hstack([crop(i) for i in row]) for row in idxs])
Image.fromarray(grid).save('_c4_wu_zoom_late.png')

idxs2 = [[12, 14, 16, 18, 20], [22, 24, 26, 28, 30]]
grid2 = np.vstack([np.hstack([crop(i) for i in row]) for row in idxs2])
Image.fromarray(grid2).save('_c4_wu_zoom_early.png')
print('ok')
