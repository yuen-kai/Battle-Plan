import numpy as np
from PIL import Image
from scipy import ndimage

def lum(x): return 0.2126*x[...,0]+0.7152*x[...,1]+0.0722*x[...,2]
def sat(x):
    mx=x.max(-1); mn=x.min(-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0)
def hue(x):
    mx=x.max(-1); mn=x.min(-1); d=mx-mn
    r,g,b=x[...,0],x[...,1],x[...,2]
    h=np.zeros_like(mx)
    m=(d>1e-6)&(mx==r); h[m]=(60*((g-b)[m]/d[m]))%360
    m=(d>1e-6)&(mx==g); h[m]=60*((b-r)[m]/d[m])+120
    m=(d>1e-6)&(mx==b); h[m]=60*((r-g)[m]/d[m])+240
    return h

def badge_box(P, pad=8):
    """the damage plate: warm saturated pixels above the unit"""
    L,S,Hh = lum(P), sat(P), hue(P)
    m = (S>0.45)&(Hh>20)&(Hh<70)&(L>90)
    lab,n = ndimage.label(ndimage.binary_dilation(m, np.ones((9,9))), np.ones((3,3)))
    if n==0: return None
    best=None
    for k in range(1,n+1):
        c = (lab==k)&m
        if c.sum()<150: continue
        ys,xs = np.nonzero(c)
        w,h = xs.max()-xs.min()+1, ys.max()-ys.min()+1
        if w<18 or h<14: continue
        if best is None or c.sum()>best[0]: best=(c.sum(), xs.min(), ys.min(), xs.max(), ys.max())
    if best is None: return None
    _,x0,y0,x1,y1 = best
    return (max(x0-pad,0), max(y0-pad,0), min(x1+pad,P.shape[1]), min(y1+pad,P.shape[0]))

shots = [('numbers','r4',1), ('numbers','r3',1), ('death','r4',1), ('death','r4',2),
         ('pogo','r4',2), ('pogo','r3',2)]
tiles=[]
for name,rnd,pi in shots:
    p = f'Captures/AbilityJuice/strips/{rnd}/{name}.jpg'
    im = Image.open(p).convert('RGB')
    W,H = im.size; pw = W//3
    pan = im.crop((pi*pw, 0, (pi+1)*pw, H))
    A = np.asarray(pan).astype(np.float32)
    bb = badge_box(A)
    print(f'{rnd}/{name} panel{pi+1}: panel size {pan.size}  badge bbox {bb}', end='')
    if bb: print('  -> w=%d h=%d  right edge frac %.4f' % (bb[2]-bb[0], bb[3]-bb[1], bb[2]/pw))
    else: print()
    if bb:
        c = pan.crop(bb)
        k = max(1, int(260/max(c.width,1)))
        tiles.append((f'{rnd}-{name}-p{pi+1}', c.resize((c.width*k, c.height*k), Image.NEAREST)))

mw = max(t[1].width for t in tiles); mh = max(t[1].height for t in tiles)
sheet = Image.new('RGB', (mw*3+40, mh*2+30), (30,30,30))
for i,(n,t) in enumerate(tiles[:6]):
    sheet.paste(t, ((i%3)*(mw+20), (i//3)*(mh+15)))
sheet.save('Captures/AbilityJuice/_ad4_num_badges.png')
print('\n', [t[0] for t in tiles], sheet.size)
