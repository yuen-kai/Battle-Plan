import numpy as np
from PIL import Image
import os

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')


def load(shot, i):
    return Image.open(os.path.join(SH, shot, 'f%04d.jpg' % i)).convert('RGB')


# very tight, 1.6 cells at death pitch 218 -> 349 px, upscaled
p = 218.0
half = int(1.6*p/2)
cx, cy = 500, 490
frames = [10, 11, 12, 13, 14, 15]
OUT = 340
s = Image.new('RGB', (OUT*len(frames)+8*(len(frames)-1), OUT), (255, 255, 255))
for k, f in enumerate(frames):
    im = load('death', f).crop((cx-half, cy-half, cx+half, cy+half)).resize((OUT, OUT), Image.LANCZOS)
    s.paste(im, (k*(OUT+8), 0))
s.save(os.path.join(BASE, '_ad_death_pop.png'))
print('saved', s.size)

# how much did the unit-body pixels change between consecutive frames?
def arr(i):
    return np.asarray(load('death', i)).astype(np.float32)
prev = None
for i in range(9, 20):
    a = arr(i)
    if prev is not None:
        d = np.abs(a-prev).max(-1)
        print('f%02d->f%02d  px changed>26: %6d   >80: %6d' % (i-1, i, (d > 26).sum(), (d > 80).sum()))
    prev = a
