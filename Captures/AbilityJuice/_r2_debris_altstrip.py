import numpy as np
from PIL import Image, ImageDraw
import os
ROOT=os.path.dirname(os.path.abspath(__file__))
R2=os.path.join(ROOT,'shots/r2/debris')
T0=12; FPS=30.
# What the impact panel looks like at the frame the picker chose vs the mass peak
ims=[]
for i,lbl in [(12,'as shipped: +0.00s (picker choice)'),(14,'+0.07s'),(17,'+0.17s'),(19,'+0.23s (mass peak)')]:
    im=Image.open(os.path.join(R2,'f%04d.jpg'%i)).convert('RGB').resize((512,512), Image.LANCZOS)
    ims.append((lbl,im))
sh=Image.new('RGB',(512*4+30, 512+22),(20,20,20)); d=ImageDraw.Draw(sh)
for k,(lbl,im) in enumerate(ims):
    sh.paste(im,(k*(512+10),22)); d.text((k*(512+10)+4,4), lbl, fill=(255,255,0))
sh.save(os.path.join(ROOT,'_r2_debris_altimpact.jpg'), quality=94)

# hi-res crop of geometry: chunks at apex, and the settled 'coins'
for i,out in [(15,'_r2_debris_geo_apex.jpg'),(25,'_r2_debris_geo_settled.jpg')]:
    im=Image.open(os.path.join(R2,'f%04d.jpg'%i)).convert('RGB')
    im.crop((300,220,760,680)).resize((920,920), Image.LANCZOS).save(os.path.join(ROOT,out), quality=95)
print('ok')
