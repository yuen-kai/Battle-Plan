import numpy as np
from PIL import Image, ImageDraw

D = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
D1 = 'Captures/AbilityJuice/shots/r1/hitreact/f%04d.jpg'
box = (330, 380, 730, 620)   # x0,y0,x1,y1  400x240

def sheet(path, frames, out, scale=2):
    w = (box[2]-box[0])*scale
    h = (box[3]-box[1])*scale
    img = Image.new('RGB', (w, h*len(frames)+6*(len(frames)-1)), (10, 11, 15))
    d = ImageDraw.Draw(img)
    for i, f in enumerate(frames):
        t = Image.open(path % f).convert('RGB').crop(box).resize((w, h), Image.LANCZOS)
        img.paste(t, (0, i*(h+6)))
        d.text((6, i*(h+6)+4), f'f{f}  t={(f-12)/30.0:+.3f}s', fill=(255, 240, 120))
    img.save(out)
    print('wrote', out, img.size)

sheet(D, [8, 12, 13, 14, 15, 17], 'Captures/AbilityJuice/_c3_hr_a.png')
sheet(D, [8, 20, 26, 33, 42, 43], 'Captures/AbilityJuice/_c3_hr_b.png')
sheet(D1, [8, 12, 13, 14, 15, 17], 'Captures/AbilityJuice/_c3_hr_r1a.png')
