import numpy as np
from PIL import Image, ImageDraw
from _c4_wu_setup import t_of

# verify strip panel == full frame downscaled 2x
strip = Image.open('strips/r3/windup.jpg')
p1 = strip.crop((0, 0, 512, 512))
raw22 = Image.open('shots/r3/windup/f0022.jpg').resize((512, 512), Image.LANCZOS)
d = np.abs(np.asarray(p1, float) - np.asarray(raw22, float)).mean()
print('strip panel1 vs f0022 downscaled: mean abs diff =', round(d, 2))

# caster zoom
def cz(i):
    im = Image.open(f'shots/r3/windup/f{i:04d}.jpg').convert('RGB')
    c = im.crop((0, 400, 300, 620)).resize((360, 264), Image.LANCZOS)
    dr = ImageDraw.Draw(c)
    dr.rectangle([0, 0, 359, 16], fill=(0, 0, 0))
    dr.text((4, 4), f'{t_of(i):+.3f}s', fill=(255, 255, 0))
    return np.asarray(c)

rows = [[12, 22, 30, 34], [35, 37, 39, 40], [42, 44, 45, 46]]
Image.fromarray(np.vstack([np.hstack([cz(i) for i in r]) for r in rows])).save('_c4_wu_caster.png')
print('saved caster zoom')
