import numpy as np
from PIL import Image, ImageFilter
O='Captures/AbilityJuice/'
# thumbnail row: r2 / r3 / two references at strip-thumb size, plus greyscale of r3
def th(p,w=380,grey=False):
    im=Image.open(p).convert('L' if grey else 'RGB')
    if grey: im=im.convert('RGB')
    return im.resize((w,int(im.height*w/im.width)), Image.LANCZOS)
rows=[('r2 shockwave', th(O+'strips/r2/shockwave.jpg')),
      ('r3 shockwave', th(O+'strips/r3/shockwave.jpg')),
      ('r3 GREYSCALE', th(O+'strips/r3/shockwave.jpg',grey=True)),
      ('CM clash-04',  th(O+'strips/reference/cm-clashabilities-04.jpg')),
      ('CM 8new-19',   th(O+'strips/reference/cm-8newabilities-19.jpg'))]
W=max(r.width for _,r in rows); H=sum(r.height+6 for _,r in rows)
c=Image.new('RGB',(W,H),(0,0,0)); y=0
for _,r in rows: c.paste(r,(0,y)); y+=r.height+6
c.save(O+'_c3_sw_thumbs.png'); print('thumbs',c.size)

# panel 3 comparison at 1:1
a=Image.open(O+'shots/r2/shockwave/f0030.jpg').convert('RGB').crop((250,380,850,860))
b=Image.open(O+'shots/r3/shockwave/f0030.jpg').convert('RGB').crop((250,380,850,860))
p=Image.new('RGB',(a.width*2+10,a.height),(0,0,0)); p.paste(a,(0,0)); p.paste(b,(a.width+10,0))
p.save(O+'_c3_sw_panel3.png'); print('panel3',p.size)
