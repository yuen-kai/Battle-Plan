import os
from PIL import Image

BASE = os.path.dirname(os.path.abspath(__file__))
R3 = os.path.join(BASE, 'strips', 'r3')
R2 = os.path.join(BASE, 'strips', 'r2')


def panel(path, k):
    im = Image.open(path).convert('RGB')
    w, h = im.size
    pw = w // 3
    return im.crop((k*pw, 0, (k+1)*pw, h))


d1 = panel(os.path.join(R3, 'death.jpg'), 1)
p1 = panel(os.path.join(R3, 'pogo.jpg'), 1)
d2 = panel(os.path.join(R3, 'death.jpg'), 2)
p2 = panel(os.path.join(R3, 'pogo.jpg'), 2)
old = panel(os.path.join(R2, 'shockwave.jpg'), 1)

W, H = d1.size
sheet = Image.new('RGB', (W*2+8, H*2+8), (255, 255, 255))
sheet.paste(d1, (0, 0)); sheet.paste(p1, (W+8, 0))
sheet.paste(d2, (0, H+8)); sheet.paste(p2, (W+8, H+8))
sheet.save(os.path.join(BASE, '_ad_strip_pairs.png'))
print('strip pairs', sheet.size, 'panel', (W, H))

# thumbnail row of the four, true small size
th = Image.new('RGB', (4*128+30, 128), (24, 24, 24))
for k, im in enumerate([d1, p1, d2, p2]):
    th.paste(im.resize((128, 128), Image.LANCZOS), (k*(128+10), 0))
th.resize(((4*128+30)*2, 256), Image.NEAREST).save(os.path.join(BASE, '_ad_strip_thumbs.png'))

# old shared shockwave vs new death vs new pogo
row = Image.new('RGB', (W*3+16, H), (255, 255, 255))
row.paste(old, (0, 0)); row.paste(d1, (W+8, 0)); row.paste(p1, (2*W+16, 0))
row.save(os.path.join(BASE, '_ad_old_vs_new.png'))
print('done')
