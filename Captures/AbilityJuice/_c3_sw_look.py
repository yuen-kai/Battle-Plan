import numpy as np
from PIL import Image, ImageDraw, ImageFilter

O = 'Captures/AbilityJuice/'

# 1. zoomed crop of the r3 impact frame vs r2 impact frame, side by side
def crop(rd, f, box=(200,320,900,900)):
    im = Image.open(O+'shots/%s/shockwave/f%04d.jpg'%(rd,f)).convert('RGB')
    return im.crop(box)

a = crop('r2',17); b = crop('r3',17)
W,H = a.size
sbs = Image.new('RGB',(W*2+12,H),(0,0,0))
sbs.paste(a,(0,0)); sbs.paste(b,(W+12,0))
sbs.save(O+'_c3_sw_zoom.png')
print('zoom', sbs.size)

# 2. squint test: our r3 strip vs the strongest reference, both blurred + downscaled
def squint(p, w=430):
    im = Image.open(p).convert('RGB')
    h = int(im.height * w/im.width)
    return im.resize((w,h), Image.LANCZOS).filter(ImageFilter.GaussianBlur(2.2))

rows = [squint(O+'strips/r2/shockwave.jpg'),
        squint(O+'strips/r3/shockwave.jpg'),
        squint(O+'strips/reference/cm-clashabilities-04.jpg'),
        squint(O+'strips/reference/cm-8newabilities-12.jpg')]
W = max(r.width for r in rows); H = sum(r.height for r in rows) + 8*len(rows)
sq = Image.new('RGB',(W,H),(0,0,0)); y=0
for r in rows:
    sq.paste(r,(0,y)); y += r.height+8
sq.save(O+'_c3_sw_squint.png')
print('squint', sq.size)

# 3. greyscale-only version of our r3 strip (does it read without colour?)
g = Image.open(O+'strips/r3/shockwave.jpg').convert('L').convert('RGB')
g.resize((776,256), Image.LANCZOS).save(O+'_c3_sw_grey.png')

# 4. tight crop on the rim so the colour band is visible at 1:1
Image.open(O+'shots/r3/shockwave/f0017.jpg').convert('RGB').crop((200,420,520,760)).resize((640,680), Image.NEAREST).save(O+'_c3_sw_rimzoom.png')
print('done')
