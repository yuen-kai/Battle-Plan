import numpy as np, os
from PIL import Image, ImageDraw
ROOT = os.path.dirname(os.path.abspath(__file__))
def refim(name): return Image.open(os.path.join(ROOT,'strips','reference',name)).convert('RGB')

def gridcrop(name, box, s, step=20):
    im = refim(name).crop(box)
    im = im.resize((im.width*s, im.height*s), Image.NEAREST)
    d = ImageDraw.Draw(im)
    x0,y0,_,_ = box
    for gx in range(box[0] - box[0]%step, box[2], step):
        X = (gx-x0)*s
        d.line([(X,0),(X,im.height)], fill=(0,255,255), width=1)
        d.text((X+2,2), str(gx), fill=(0,255,255))
    for gy in range(box[1] - box[1]%step, box[3], step):
        Y = (gy-y0)*s
        d.line([(0,Y),(im.width,Y)], fill=(255,0,255), width=1)
        d.text((2,Y+2), str(gy), fill=(255,0,255))
    return im

gridcrop('cm-everyability-20.jpg',(1150,80,1400,320),4,20).save(os.path.join(ROOT,'_ad3_grid20.png'))
gridcrop('cm-everyability-22.jpg',(1150,40,1400,220),4,20).save(os.path.join(ROOT,'_ad3_grid22.png'))
print('ok')
