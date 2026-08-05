import numpy as np, os
from PIL import Image, ImageDraw
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

pl = plate('r3','numbers')
L,S,H = lum(pl), sat(pl), hue(pl)
# scan rows 360..470 in the unit's x window, report what is present
xs0,xs1 = 500, 730
for y in range(380, 470, 4):
    row = pl[y, xs0:xs1]
    Lr, Sr, Hr = lum(row), sat(row), hue(row)
    green = ((Sr>0.30)&(Hr>90)&(Hr<170)).sum()
    pink  = ((Sr>0.30)&((Hr>320)|(Hr<25))).sum()
    dark  = (Lr<60).sum()
    print('y=%3d green=%3d pink=%3d dark=%3d minL=%3.0f' % (y, green, pink, dark, Lr.min()))

# annotate a crop showing badge bottom + unit top at f013/f014
for idx in (13,14,20):
    fr = load(fp('r3','numbers',idx))
    im = Image.fromarray(fr.astype(np.uint8)).crop((480,180,760,470))
    im = im.resize((im.width*3, im.height*3), Image.NEAREST)
    d = ImageDraw.Draw(im)
    for y in range(180,470,10):
        Y=(y-180)*3
        d.line([(0,Y),(im.width,Y)], fill=(0,255,255) if y%50 else (255,0,255), width=1)
        d.text((3,Y+1), str(y), fill=(0,255,255))
    im.save(os.path.join(ROOT,'_ad3_gap_f%02d.png'%idx))
print('saved gap crops')
