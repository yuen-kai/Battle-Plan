import numpy as np, os
from PIL import Image
ROOT = os.path.dirname(os.path.abspath(__file__))
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(-1); mn=a.min(-1)
    return np.where(mx>1e-5,(mx-mn)/np.maximum(mx,1e-5),0.0)
def hue(a):
    mx=a.max(-1); mn=a.min(-1); d=mx-mn
    r,g,b=a[...,0],a[...,1],a[...,2]; h=np.zeros_like(mx); m=d>1e-6
    i=m&(mx==r); h[i]=((g-b)[i]/d[i])%6
    i=m&(mx==g); h[i]=((b-r)[i]/d[i])+2
    i=m&(mx==b); h[i]=((r-g)[i]/d[i])+4
    return h*60.0

def ref(name):
    return np.asarray(Image.open(os.path.join(ROOT,'strips','reference',name)).convert('RGB')).astype(float)

def crop_img(name, box, s=6):
    im = Image.open(os.path.join(ROOT,'strips','reference',name)).convert('RGB').crop(box)
    return im.resize((im.width*s, im.height*s), Image.NEAREST)

S = 1552/1024.0
jobs = [
    ('cm-everyability-20.jpg', (int(780*S), int(60*S), int(900*S), int(200*S)), 5),
    ('cm-everyability-22.jpg', (int(770*S), int(35*S), int(890*S), int(130*S)), 5),
]
tiles = [crop_img(*j) for j in jobs]
W = sum(t.width for t in tiles)+20*(len(tiles)-1); H = max(t.height for t in tiles)
out = Image.new('RGB',(W,H),(12,12,14)); x=0
for t in tiles: out.paste(t,(x,0)); x+=t.width+20
out.save(os.path.join(ROOT,'_ad3_ref_numzoom.png'))
print('saved', out.size, jobs)
