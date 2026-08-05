import numpy as np, os
from PIL import Image, ImageDraw

R4='Captures/AbilityJuice/shots/r4/death'
def F(i): return Image.open('%s/f%04d.jpg'%(R4,i)).convert('RGB')
def A(i): return np.asarray(F(i)).astype(np.float32)

# find the death centre: biggest change between f10 and f18
a10,a18 = A(10), A(18)
d = np.abs(a10-a18).max(axis=-1)
ys,xs = np.where(d>40)
cy,cx = int(ys.mean()), int(xs.mean())
print('change centroid', cx, cy, 'bbox', xs.min(), xs.max(), ys.min(), ys.max())

# fixed crop window around it
W=300
x0,y0 = cx-W//2, cy-W//2
print('crop', x0,y0,W)

frames = list(range(10,29))
cols=len(frames)
th=260
sheet = Image.new('RGB',(th*cols, th+22),(10,10,10))
dr=ImageDraw.Draw(sheet)
for k,i in enumerate(frames):
    c = F(i).crop((x0,y0,x0+W,y0+W)).resize((th,th), Image.LANCZOS)
    sheet.paste(c,(k*th,22))
    dr.text((k*th+6,6),'f%d  %+.3fs'%(i,(i-12)/30.0),fill=(255,255,255))
sheet.save('Captures/AbilityJuice/_ad4_death_zoom.jpg',quality=92)
print('wrote zoom sheet', sheet.size)

# two halves for legibility
sheet.crop((0,0,th*10,th+22)).save('Captures/AbilityJuice/_ad4_death_zoomA.jpg',quality=93)
sheet.crop((th*9,0,th*cols,th+22)).save('Captures/AbilityJuice/_ad4_death_zoomB.jpg',quality=93)
