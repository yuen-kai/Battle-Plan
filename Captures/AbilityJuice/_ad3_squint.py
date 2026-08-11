import numpy as np, os
from PIL import Image
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

def strip(rnd,name): return Image.open(os.path.join(ROOT,'strips',rnd,name)).convert('RGB')
def refstrip(name): return Image.open(os.path.join(ROOT,'strips','reference',name)).convert('RGB')

# 1) thumbnails of the three strips + a CM reference, all at 300px wide
rows = [('r2 numbers', strip('r2','numbers.jpg')),
        ('r3 numbers', strip('r3','numbers.jpg')),
        ('r3 death',   strip('r3','death.jpg')),
        ('r3 pogo',    strip('r3','pogo.jpg')),
        ('CM ability-22', refstrip('cm-everyability-22.jpg')),
        ('CM ability-20', refstrip('cm-everyability-20.jpg'))]
W = 480
tiles=[]
for lbl,im in rows:
    t = im.resize((W, int(im.height*W/im.width)), Image.LANCZOS)
    tiles.append((lbl,t))
H = sum(t.height+6 for _,t in tiles)
out = Image.new('RGB',(W,H),(10,10,12)); y=0
for lbl,t in tiles: out.paste(t,(0,y)); y+=t.height+6
out.save(os.path.join(ROOT,'_ad3_squint.png'))

# 2) greyscale of the same
g = out.convert('L').convert('RGB')
g.save(os.path.join(ROOT,'_ad3_squint_grey.png'))

# 3) matched-cell-scale: our badge (cell=251px) vs CM "5" (cell=~62px), both to cell=200px
fr = load(fp('r3','numbers',20))
ours = Image.fromarray(fr.astype(np.uint8)).crop((510,175,730,360))
s_ours = 200.0/251.0
ours = ours.resize((max(1,int(ours.width*s_ours)), max(1,int(ours.height*s_ours))), Image.LANCZOS)

cm = refstrip('cm-everyability-22.jpg').crop((1190,52,1250,115))
s_cm = 200.0/62.0
cm = cm.resize((int(cm.width*s_cm), int(cm.height*s_cm)), Image.LANCZOS)

cm2 = refstrip('cm-everyability-20.jpg').crop((1330,240,1380,280))
cm2 = cm2.resize((int(cm2.width*200/62), int(cm2.height*200/62)), Image.LANCZOS)

H = max(ours.height, cm.height, cm2.height)
out2 = Image.new('RGB',(ours.width+cm.width+cm2.width+40, H),(120,124,130))
out2.paste(ours,(0,H-ours.height)); out2.paste(cm,(ours.width+20,H-cm.height))
out2.paste(cm2,(ours.width+cm.width+40,H-cm2.height))
out2.save(os.path.join(ROOT,'_ad3_matched.png'))
print('saved', out.size, out2.size)
