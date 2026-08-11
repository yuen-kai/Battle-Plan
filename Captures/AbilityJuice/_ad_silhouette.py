import numpy as np
from PIL import Image
import os

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')
PITCH = {'death': 218.0, 'pogo': 158.0}


def crop(shot, idx, cx, cy, cells, out, grey=False):
    p = PITCH[shot]; half = int(cells*p/2)
    im = Image.open(os.path.join(SH, shot, 'f%04d.jpg' % idx)).convert('RGB')
    W, H = im.size
    x0 = max(0, int(cx-half)); y0 = max(0, int(cy-half))
    x1 = min(W, int(cx+half)); y1 = min(H, int(cy+half))
    c = im.crop((x0, y0, x1, y1))
    bg = Image.new('RGB', (2*half, 2*half), (186, 196, 201))
    bg.paste(c, (x0-int(cx-half), y0-int(cy-half)))
    if grey:
        bg = bg.convert('L').convert('RGB')
    return bg.resize((out, out), Image.LANCZOS)


CELLS, OUT = 3.0, 400
items = [('death', 13, (508, 500)), ('pogo', 45, (762, 505)),
         ('death', 26, (508, 505)), ('pogo', 58, (772, 505))]
for grey, name in ((False, '_ad_final_pairs.png'), (True, '_ad_final_grey.png')):
    s = Image.new('RGB', (OUT*4+30, OUT), (255, 255, 255))
    for k, (shot, f, c) in enumerate(items):
        s.paste(crop(shot, f, c[0], c[1], CELLS, OUT, grey), (k*(OUT+10), 0))
    s.save(os.path.join(BASE, name))
print('saved')
