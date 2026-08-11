import numpy as np
from PIL import Image

a = Image.open('Captures/AbilityJuice/strips/r4/windup.jpg').convert('RGB')
b = Image.open('Captures/AbilityJuice/strips/r3/windup.jpg').convert('RGB')
W,H = a.size
pw = W//3

# caster sits left-of-centre in panels 1 and 2
crops = {'p1': (0, 90, 220, 260), 'p2': (pw+0, 90, pw+220, 260)}
tiles = []
for k,(x0,y0,x1,y1) in crops.items():
    for img,tag in ((b,'r3'),(a,'r4')):
        c = img.crop((x0,y0,x1,y1)).resize(((x1-x0)*3,(y1-y0)*3), Image.NEAREST)
        tiles.append((f'{tag}-{k}', c))

cw, ch = tiles[0][1].size
sheet = Image.new('RGB', (cw*2+24, ch*2+24), (0,0,0))
order = [('r3-p1',0,0),('r4-p1',1,0),('r3-p2',0,1),('r4-p2',1,1)]
d = dict(tiles)
for name,cx,cy in order:
    sheet.paste(d[name], (cx*(cw+24), cy*(ch+24)))
sheet.save('Captures/AbilityJuice/_ad4_wu_zoom.png')
print('saved', sheet.size)

# unit body vs charge size: measure the yellow charge extent in panel 1 and 2 of r4
A = np.asarray(a).astype(np.float32)
def sat(x):
    mx=x.max(-1); mn=x.min(-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0)
def lum(x): return 0.2126*x[...,0]+0.7152*x[...,1]+0.0722*x[...,2]
def hue(x):
    mx=x.max(-1); mn=x.min(-1); d=mx-mn
    r,g,bl=x[...,0],x[...,1],x[...,2]
    h=np.zeros_like(mx)
    m=(d>1e-6)&(mx==r); h[m]=(60*((g-bl)[m]/d[m]))%360
    m=(d>1e-6)&(mx==g); h[m]=60*((bl-r)[m]/d[m])+120
    m=(d>1e-6)&(mx==bl); h[m]=60*((r-g)[m]/d[m])+240
    return h

for i in range(3):
    P = A[:, i*pw:(i+1)*pw]
    L,S,Hh = lum(P), sat(P), hue(P)
    # hot charge: bright and yellow
    m = (L>200)&(S>0.45)&(Hh>25)&(Hh<75)
    if m.sum()<10:
        print('panel%d: no hot charge pixels'%(i+1)); continue
    ys,xs = np.nonzero(m)
    print('panel%d hot-charge bbox w=%dpx h=%dpx  area=%dpx  centroid=(%.0f,%.0f)  medhue=%.0f medsat=%.2f' % (
        i+1, xs.max()-xs.min()+1, ys.max()-ys.min()+1, m.sum(), xs.mean(), ys.mean(),
        np.median(Hh[m]), np.median(S[m])))
    # broader charge body incl. non-clipping amber
    m2 = (S>0.45)&(Hh>25)&(Hh<75)&(L>120)
    ys2,xs2 = np.nonzero(m2)
    print('        broad amber bbox w=%dpx h=%dpx area=%dpx' % (
        xs2.max()-xs2.min()+1, ys2.max()-ys2.min()+1, m2.sum()))
