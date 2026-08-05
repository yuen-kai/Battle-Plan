import numpy as np, os
from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))
def load(p): return Image.open(p).convert('RGB')

def crop(rnd, shot, idx, box, scale=3):
    p = os.path.join(ROOT, 'shots', rnd, shot, 'f%04d.jpg' % idx)
    im = load(p).crop(box)
    return im.resize((im.width*scale, im.height*scale), Image.NEAREST)

# r3 badge box at money frame, generous
box3 = (470, 175, 760, 420)
box2 = (470, 115, 760, 400)

a = crop('r2', 'numbers', 14, box2, 3)
b = crop('r3', 'numbers', 14, box3, 3)
H = max(a.height, b.height)
out = Image.new('RGB', (a.width + b.width + 24, H), (20,20,24))
out.paste(a, (0, 0)); out.paste(b, (a.width+24, 0))
out.save(os.path.join(ROOT, '_ad3_num_zoom_r2r3.png'))
print('saved', out.size)

# r3 timeline of badge, frames 12..22
idxs = [12,13,14,15,16,18,20,24,28]
tiles = [crop('r3','numbers', i, (480,170,770,420), 2) for i in idxs]
w = tiles[0].width; h = tiles[0].height
tl = Image.new('RGB', (w*len(tiles) + 6*(len(tiles)-1), h), (20,20,24))
for k,t in enumerate(tiles):
    tl.paste(t, (k*(w+6), 0))
tl.save(os.path.join(ROOT, '_ad3_num_timeline.png'))
print('saved timeline', tl.size, idxs)
