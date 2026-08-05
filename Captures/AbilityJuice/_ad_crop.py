import numpy as np
from PIL import Image
import os

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')
PITCH = {'death': 218.0, 'pogo': 158.0}


def load(shot, idx):
    return Image.open(os.path.join(SH, shot, 'f%04d.jpg' % idx)).convert('RGB')


def crop_cells(shot, idx, cx, cy, cells):
    p = PITCH[shot]
    half = int(cells*p/2)
    im = load(shot, idx)
    box = (int(cx-half), int(cy-half), int(cx+half), int(cy+half))
    return im.crop(box)


# 5 cells wide window centred on each event
DEATH_C = (518, 470)
POGO_C = (770, 500)
CELLS = 5.0
OUT = 512

pairs = [
    ('death', 8, DEATH_C, 'D windup -0.17'),
    ('death', 13, DEATH_C, 'D impact +0.00'),
    ('death', 16, DEATH_C, 'D +0.10'),
    ('death', 26, DEATH_C, 'D after +0.43'),
    ('pogo', 40, POGO_C, 'P windup -0.17'),
    ('pogo', 45, POGO_C, 'P impact +0.00'),
    ('pogo', 48, POGO_C, 'P +0.10'),
    ('pogo', 58, POGO_C, 'P after +0.43'),
]

tiles = []
for shot, idx, c, label in pairs:
    im = crop_cells(shot, idx, c[0], c[1], CELLS).resize((OUT, OUT), Image.LANCZOS)
    tiles.append((label, im))

sheet = Image.new('RGB', (OUT*4, OUT*2), (0, 0, 0))
for k, (label, im) in enumerate(tiles):
    sheet.paste(im, ((k % 4)*OUT, (k//4)*OUT))
sheet.save(os.path.join(BASE, '_ad_matched.png'))
print('saved matched', sheet.size, 'window =', CELLS, 'cells')

# thumbnail squint test: impact frames only, tiny
d = crop_cells('death', 13, *DEATH_C, CELLS).resize((96, 96), Image.LANCZOS)
p = crop_cells('pogo', 45, *POGO_C, CELLS).resize((96, 96), Image.LANCZOS)
d2 = crop_cells('death', 16, *DEATH_C, CELLS).resize((96, 96), Image.LANCZOS)
p2 = crop_cells('pogo', 48, *POGO_C, CELLS).resize((96, 96), Image.LANCZOS)
sq = Image.new('RGB', (96*4+30, 96), (20, 20, 20))
for k, im in enumerate([d, p, d2, p2]):
    sq.paste(im, (k*(96+10), 0))
sq.resize((96*4*3+90, 96*3), Image.NEAREST).save(os.path.join(BASE, '_ad_squint.png'))
print('saved squint')
