import numpy as np
from PIL import Image, ImageDraw

R4='Captures/AbilityJuice/shots/r4/death'
def F(i): return Image.open('%s/f%04d.jpg'%(R4,i)).convert('RGB')
x0,y0,W = 369,312,300
th=340

def sheet(frames,name,cols=5):
    rows=(len(frames)+cols-1)//cols
    s=Image.new('RGB',(th*cols, (th+24)*rows),(12,12,12))
    dr=ImageDraw.Draw(s)
    for k,i in enumerate(frames):
        r,c=k//cols,k%cols
        s.paste(F(i).crop((x0,y0,x0+W,y0+W)).resize((th,th),Image.LANCZOS),(c*th,r*(th+24)+24))
        dr.text((c*th+8,r*(th+24)+7),'f%d   %+.3fs'%(i,(i-12)/30.0),fill=(255,255,90))
    s.save('Captures/AbilityJuice/%s'%name,quality=94)
    print(name, s.size)

sheet([9,10,11,12,13],'_ad4_d_pre.jpg')
sheet([13,14,15,16,17],'_ad4_d_sink.jpg')
sheet([18,20,22,24,26],'_ad4_d_hold.jpg')
