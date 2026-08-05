import numpy as np, os
from PIL import Image, ImageDraw
ROOT = os.path.dirname(os.path.abspath(__file__))

def strip(rnd, name): return Image.open(os.path.join(ROOT,'strips',rnd,name)).convert('RGB')

def panel(rnd, name, k):
    im = strip(rnd,name); W,H = im.size
    pw = (W-16)//3
    x0 = k*(pw+8)
    return im.crop((x0,0,x0+pw,H))

# pogo panel 3 right edge, zoomed
p = panel('r3','pogo.jpg',2)
print('pogo panel size', p.size)
z = p.crop((p.width-190, 60, p.width, 240))
z = z.resize((z.width*4, z.height*4), Image.NEAREST)
z.save(os.path.join(ROOT,'_ad3_pogo_clip.png'))

d = panel('r3','death.jpg',1)
z2 = d.crop((100,20,330,220)); z2 = z2.resize((z2.width*3,z2.height*3), Image.NEAREST)
z2.save(os.path.join(ROOT,'_ad3_death_p2.png'))
d3 = panel('r3','death.jpg',2)
z3 = d3.crop((60,20,330,240)); z3 = z3.resize((z3.width*3,z3.height*3), Image.NEAREST)
z3.save(os.path.join(ROOT,'_ad3_death_p3.png'))
print('ok')
