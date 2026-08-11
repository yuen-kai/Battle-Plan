import numpy as np
from PIL import Image, ImageDraw
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(ROOT, 'shots', 'r2', 'windup')
frames = sorted(f for f in os.listdir(SH) if f.endswith('.jpg'))


def load(i):
    return Image.open(os.path.join(SH, frames[i])).convert('RGB')


# --- A: timeline montage, full frames, key beats ---
keys = [10, 12, 14, 16, 20, 22, 28, 34, 40, 44, 45, 46, 48, 54]
tile = 300
m = Image.new('RGB', (tile*7, tile*2 + 40), (12, 12, 12))
d = ImageDraw.Draw(m)
for n, i in enumerate(keys):
    im = load(i).resize((tile, tile), Image.LANCZOS)
    x = (n % 7)*tile; y = (n//7)*(tile+20)
    m.paste(im, (x, y))
    d.text((x+4, y+tile+4), "f%d t=%+.2f" % (i, i/30.0-0.40), fill=(255, 255, 0))
m.save(os.path.join(ROOT, '_c2_windup_timeline.png'))

# --- B: caster close-up over time (caster is on the LEFT side) ---
CROP = (60, 380, 460, 620)   # x0,y0,x1,y1 around left caster
ck = [10, 14, 18, 22, 26, 30, 34, 38, 40, 42, 44, 45, 46, 47, 48, 50]
cw, ch = CROP[2]-CROP[0], CROP[3]-CROP[1]
sc = 2
mm = Image.new('RGB', (cw*sc*2, (ch*sc+18)*8), (12, 12, 12))
dd = ImageDraw.Draw(mm)
for n, i in enumerate(ck):
    im = load(i).crop(CROP).resize((cw*sc, ch*sc), Image.LANCZOS)
    x = (n % 2)*cw*sc; y = (n//2)*(ch*sc+18)
    mm.paste(im, (x, y))
    dd.text((x+4, y+2), "f%d t=%+.2f" % (i, i/30.0-0.40), fill=(255, 255, 0))
mm.save(os.path.join(ROOT, '_c2_windup_caster.png'))

# --- C: the three strip panels, upscaled ---
for name, i in (('p1', 22), ('p2', 40), ('p3', 54)):
    load(i).resize((760, 760), Image.LANCZOS).save(os.path.join(ROOT, '_c2_windup_%s.png' % name))

# --- D: squint test (downsample hard) ---
sq = Image.new('RGB', (128*3+16, 128), (0, 0, 0))
for n, i in enumerate((22, 40, 54)):
    sq.paste(load(i).resize((128, 128), Image.BOX), (n*(128+8), 0))
sq.resize((int((128*3+16)*2.2), int(128*2.2)), Image.NEAREST).save(os.path.join(ROOT, '_c2_windup_squint.png'))
print("wrote montages")
