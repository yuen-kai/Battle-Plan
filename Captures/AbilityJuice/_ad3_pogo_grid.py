import numpy as np, os
from PIL import Image, ImageDraw
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

def gridded(rnd, shot, idx, box, s=4, step=20):
    fr = load(fp(rnd,shot,idx))
    im = Image.fromarray(fr.astype(np.uint8)).crop(box)
    im = im.resize((im.width*s, im.height*s), Image.NEAREST)
    d = ImageDraw.Draw(im)
    for gx in range(box[0]-box[0]%step, box[2], step):
        X=(gx-box[0])*s; d.line([(X,0),(X,im.height)],fill=(0,255,255)); d.text((X+2,2),str(gx),fill=(0,255,255))
    for gy in range(box[1]-box[1]%step, box[3], step):
        Y=(gy-box[1])*s; d.line([(0,Y),(im.width,Y)],fill=(255,0,255)); d.text((2,Y+2),str(gy),fill=(255,0,255))
    return im

gridded('r3','pogo',58,(900,330,1024,460),6,20).save(os.path.join(ROOT,'_ad3_pogo58.png'))
gridded('r3','pogo',50,(880,330,1024,470),6,20).save(os.path.join(ROOT,'_ad3_pogo50.png'))
gridded('r3','death',13,(430,140,760,420),3,20).save(os.path.join(ROOT,'_ad3_death13.png'))
print('ok')
