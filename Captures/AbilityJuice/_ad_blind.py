import os
from PIL import Image

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')
PITCH = {'death': 218.0, 'pogo': 158.0}


def crop(shot, idx, cx, cy, cells, out):
    p = PITCH[shot]; half = int(cells*p/2)
    im = Image.open(os.path.join(SH, shot, 'f%04d.jpg' % idx)).convert('RGB')
    W, H = im.size
    x0 = max(0, int(cx-half)); y0 = max(0, int(cy-half))
    x1 = min(W, int(cx+half)); y1 = min(H, int(cy+half))
    c = im.crop((x0, y0, x1, y1))
    bg = Image.new('RGB', (2*half, 2*half), (185, 195, 200))
    bg.paste(c, (x0-int(cx-half), y0-int(cy-half)))
    return bg.resize((out, out), Image.LANCZOS)


# tight 2.2-cell window on the material only; death centred below the badge,
# pogo centred on the landing point. Same world size, same output size.
CELLS = 2.2
OUT = 380
items = [
    ('death', 13, (505, 500), 'A'),
    ('pogo', 45, (760, 505), 'B'),
    ('death', 20, (505, 500), 'C'),
    ('pogo', 48, (760, 505), 'D'),
    ('death', 26, (505, 505), 'E'),
    ('pogo', 58, (770, 505), 'F'),
]
s = Image.new('RGB', (OUT*len(items)+10*(len(items)-1), OUT), (255, 255, 255))
for k, (shot, f, c, lab) in enumerate(items):
    s.paste(crop(shot, f, c[0], c[1], CELLS, OUT), (k*(OUT+10), 0))
s.save(os.path.join(BASE, '_ad_blind.png'))
print('blind', s.size)

# and the same six at true thumbnail size
T = 72
t = Image.new('RGB', (T*len(items)+8*(len(items)-1), T), (255, 255, 255))
for k, (shot, f, c, lab) in enumerate(items):
    t.paste(crop(shot, f, c[0], c[1], CELLS, T), (k*(T+8), 0))
t.resize((t.size[0]*4, T*4), Image.NEAREST).save(os.path.join(BASE, '_ad_blind_thumb.png'))
print('blind thumb')
