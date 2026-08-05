import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import os

ROOT=os.path.dirname(os.path.abspath(__file__))
def op(p): return Image.open(os.path.join(ROOT,p)).convert('RGB')

ours   = op('strips/r2/debris.jpg')
oursr1 = op('strips/r1/debris.jpg')
ref1   = op('strips/reference/cm-clashabilities-04.jpg')
ref2   = op('strips/reference/cm-8newabilities-13.jpg')
ref3   = op('strips/reference/cm-everyability-05.jpg')

rows = [('R2 OURS',ours), ('CM clashabilities-04',ref1), ('CM 8newabilities-13',ref2), ('CM everyability-05',ref3), ('R1 OURS (rejected)',oursr1)]
W = 1552//3
# squint: downsample hard then blow back up
sheet = Image.new('RGB',(1552, len(rows)*(230)), (20,20,20))
d=ImageDraw.Draw(sheet)
for i,(nm,im) in enumerate(rows):
    small = im.resize((1552//9, 512//9), Image.LANCZOS).resize((1552,512), Image.NEAREST)
    small = small.resize((1552,512//2+40))
    sheet.paste(small.resize((1500,200)), (26, i*230+26))
    d.text((6,i*230+6), nm, fill=(255,255,0))
sheet.save(os.path.join(ROOT,'_r2_debris_squint.jpg'), quality=92)
print('wrote squint')

# side-by-side impact panels at 1:1
imp = Image.new('RGB',(W*4+30, 512+22),(20,20,20))
d=ImageDraw.Draw(imp)
for i,(nm,im) in enumerate([('OURS r2',ours),('CM clash-04',ref1),('CM 8new-13',ref2),('CM every-05',ref3)]):
    imp.paste(im.crop((520,0,520+W,512)),(i*(W+10),22))
    d.text((i*(W+10)+4,4), nm+' — IMPACT', fill=(255,255,0))
imp.save(os.path.join(ROOT,'_r2_debris_impact_row.jpg'), quality=94)

aft = Image.new('RGB',(W*4+30, 512+22),(20,20,20))
d=ImageDraw.Draw(aft)
for i,(nm,im) in enumerate([('OURS r2',ours),('CM clash-04',ref1),('CM 8new-13',ref2),('CM every-05',ref3)]):
    aft.paste(im.crop((1040,0,1040+W,512)),(i*(W+10),22))
    d.text((i*(W+10)+4,4), nm+' — AFTERMATH', fill=(255,255,0))
aft.save(os.path.join(ROOT,'_r2_debris_aftermath_row.jpg'), quality=94)
print('wrote rows')
