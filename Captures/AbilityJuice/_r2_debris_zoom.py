import numpy as np
from PIL import Image, ImageDraw
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
R2 = os.path.join(ROOT, 'shots/r2/debris')
R1 = os.path.join(ROOT, 'shots/r1/debris')
FPS=30.0; T0=12

def load(p): return Image.open(p).convert('RGB')

# crop box around impact, generous. impact centre ~ (512, 470) guess -> verify from mask
import json
rows = json.load(open(os.path.join(ROOT,'_r2_debris_R2.json')))
cx = [r['cx'] for r in rows if 'cx' in r]; cy=[r['cy'] for r in rows if 'cy' in r]
print("centroid range x", min(cx), max(cx), "y", min(cy), max(cy))
X0,X1 = min(cx)-260, max(cx)+260
Y0,Y1 = min(cy)-300, max(cy)+240
print("crop", X0,X1,Y0,Y1)

def sheet(D, idxs, out, scale=1.0, box=None):
    ims=[]
    for i in idxs:
        im = load(os.path.join(D,'f%04d.jpg'%i))
        c = im.crop(box)
        ims.append((i,c))
    w,h = ims[0][1].size
    w=int(w*scale); h=int(h*scale)
    cols=len(idxs) if len(idxs)<=5 else 5
    rowsn=(len(ims)+cols-1)//cols
    sh = Image.new('RGB',(cols*w, rowsn*(h+18)),(0,0,0))
    d=ImageDraw.Draw(sh)
    for k,(i,c) in enumerate(ims):
        r,cc = divmod(k,cols)
        sh.paste(c.resize((w,h), Image.LANCZOS),(cc*w, r*(h+18)+18))
        d.text((cc*w+4, r*(h+18)+4), "%+0.3fs (f%d)"%((i-T0)/FPS, i), fill=(255,255,0))
    sh.save(out, quality=94)
    print("wrote", out, sh.size)

box=(X0,Y0,X1,Y1)
# Key window: contact through settle
sheet(R2, [11,12,13,14,15,16,17,18,19,21], os.path.join(ROOT,'_r2_dz_early.jpg'), 1.0, box)
sheet(R2, [12,14,16,19,22,26,30,models:=33,not_used:=0][:8]+[], os.path.join(ROOT,'_r2_dz_mid.jpg'), 1.0, box) if False else None
sheet(R2, [12,14,16,19,22,26,30,33,not_:=0][:8], os.path.join(ROOT,'_r2_dz_mid.jpg'), 1.0, box)
sheet(R2, [24,26,28,30,32,63//2,36,38,40][:9], os.path.join(ROOT,'_r2_dz_late.jpg'), 1.0, box)
sheet(R1, [12,14,16,19,22,26,30,33], os.path.join(ROOT,'_r1_dz_mid.jpg'), 1.0, box)
