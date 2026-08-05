import numpy as np, glob
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

r4 = sorted(glob.glob('Captures/AbilityJuice/shots/r4/windup/f*.jpg'))
r3 = sorted(glob.glob('Captures/AbilityJuice/shots/r3/windup/f*.jpg'))
print('r3 frames', len(r3))

def unit_black(A, box):
    """dark near-black outline+body of the caster inside a box"""
    L = lum(A)
    m = L < 70
    o = np.zeros(L.shape, bool)
    o[box[1]:box[3], box[0]:box[2]] = m[box[1]:box[3], box[0]:box[2]]
    return o

CELL = 158.0
for tag, seq in (('r3', r3), ('r4', r4)):
    if not seq: continue
    for i in (22, 40):
        A = np.asarray(Image.open(seq[i]).convert('RGB')).astype(np.float32)
        L,S,Hh = lum(A), sat(A), hue(A)
        pink = (S>0.35)&((Hh>320)|(Hh<8))&(L>60)&(L<210); pink[:,512:]=False
        ys,xs = np.nonzero(pink)
        x0 = xs.min()
        box = (max(x0-40,0), 400, min(x0+240,1023), 600)
        blk = unit_black(A, box)
        yel = (L>150)&(S>0.6)&(Hh>25)&(Hh<75)
        y2 = np.zeros(L.shape,bool); y2[box[1]:box[3], box[0]:box[2]] = yel[box[1]:box[3], box[0]:box[2]]
        print('%s f%02d  caster-box dark(outline/body) px = %5d   yellow px = %5d  (yellow/dark ratio %.2f)  yellow width %.2f cells' % (
            tag, i, blk.sum(), y2.sum(), y2.sum()/max(blk.sum(),1),
            0 if y2.sum()<10 else (np.nonzero(y2)[1].max()-np.nonzero(y2)[1].min()+1)/CELL))
