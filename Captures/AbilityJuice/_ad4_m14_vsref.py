import numpy as np
from PIL import Image, ImageDraw

def panels(p):
    a=Image.open(p).convert('RGB'); w,h=a.size; pw=w//3
    return [a.crop((i*pw,0,(i+1)*pw,h)) for i in range(3)]

TH=300
def strip_row(p,label):
    ps=panels(p)
    s=Image.new('RGB',(TH*3+8, TH+22),(10,10,10)); d=ImageDraw.Draw(s)
    for k,im in enumerate(ps):
        s.paste(im.resize((TH,TH),Image.LANCZOS),(k*(TH+4),22))
    d.text((6,6),label,fill=(255,255,120))
    return s

rows=[('Captures/AbilityJuice/strips/r4/death.jpg','OURS  DEATH  (r4)'),
      ('Captures/AbilityJuice/strips/r4/pogo.jpg','OURS  POGO LANDING  (r4)'),
      ('Captures/AbilityJuice/strips/reference/cm-8newabilities-12.jpg','CLASH MINI  cm-8newabilities-12'),
      ('Captures/AbilityJuice/strips/reference/cm-everyability-22.jpg','CLASH MINI  cm-everyability-22'),
      ('Captures/AbilityJuice/strips/reference/cm-clashabilities-04.jpg','CLASH MINI  cm-clashabilities-04')]
ims=[strip_row(p,l) for p,l in rows]
W=max(i.width for i in ims); Hh=sum(i.height+6 for i in ims)
s=Image.new('RGB',(W,Hh),(10,10,10)); y=0
for i in ims: s.paste(i,(0,y)); y+=i.height+6
s.save('Captures/AbilityJuice/_ad4_vsref.jpg',quality=93)
print('wrote vsref', s.size)

# aftermath panel comparison r3 vs r4 pogo
a=Image.new('RGB',(TH*4+12,TH+22),(10,10,10)); d=ImageDraw.Draw(a)
for k,(p,lab) in enumerate([('Captures/AbilityJuice/strips/r3/pogo.jpg','R3 pogo impact'),
                            ('Captures/AbilityJuice/strips/r4/pogo.jpg','R4 pogo impact')]):
    ps=panels(p)
    a.paste(ps[1].resize((TH,TH),Image.LANCZOS),(k*2*(TH+4),22))
    a.paste(ps[2].resize((TH,TH),Image.LANCZOS),((k*2+1)*(TH+4),22))
    d.text((k*2*(TH+4)+6,6),lab+'  |  aftermath',fill=(255,255,120))
a.save('Captures/AbilityJuice/_ad4_pogo_r3r4.jpg',quality=93)
print('wrote pogo r3/r4')
