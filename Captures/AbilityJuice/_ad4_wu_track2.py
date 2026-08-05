import numpy as np, glob
from PIL import Image

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

fs = sorted(glob.glob('Captures/AbilityJuice/shots/r4/windup/f*.jpg'))
A0 = np.asarray(Image.open(fs[0]).convert('RGB')).astype(np.float32)
H,W,_ = A0.shape
LEFT = slice(0, int(W*0.5))

# --- cell pitch: telegraph tiles are the tan overlay. measure its bbox at the widest frame.
Am = np.asarray(Image.open(fs[40]).convert('RGB')).astype(np.float32)
Lm,Sm,Hm = lum(Am), sat(Am), hue(Am)
tel = (Hm>18)&(Hm<45)&(Sm>0.30)&(Sm<0.85)&(Lm>90)&(Lm<190)
ys,xs = np.nonzero(tel)
print('telegraph bbox at f40: x %d..%d (w=%d)  y %d..%d (h=%d)  area=%d'
      % (xs.min(),xs.max(),xs.max()-xs.min()+1, ys.min(),ys.max(),ys.max()-ys.min()+1, tel.sum()))

print('\n frame   t     pinkXmin pinkArea greenXmin greenArea | chargeW chargeArea')
for i in list(range(10,48)):
    A = np.asarray(Image.open(fs[i]).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    pink = np.zeros((H,W),bool)
    p = (S>0.35)&((Hh>320)|(Hh<8))&(L>60)&(L<210)
    pink[:,LEFT] = p[:,LEFT]
    ys,xs = np.nonzero(pink)
    sel = xs < xs.min()+95
    pxmin, parea = xs.min(), int(sel.sum())
    # caster health bar = saturated green
    grn = np.zeros((H,W),bool)
    g = (Hh>80)&(Hh<160)&(S>0.45)&(L>70)
    grn[:,LEFT] = g[:,LEFT]
    gy,gx = np.nonzero(grn)
    gxmin, garea = (gx.min(), int(grn.sum())) if len(gx) else (-1,0)
    ch = np.zeros((H,W),bool)
    c = (L>200)&(S>0.45)&(Hh>25)&(Hh<75); ch[:,LEFT]=c[:,LEFT]
    cy2,cx2 = np.nonzero(ch)
    cw = (cx2.max()-cx2.min()+1) if ch.sum()>5 else 0
    print('%5d %+6.3f   %6d %8d %8d %8d  | %6d %8d%s' % (
        i, i/30.0-0.4, pxmin, parea, gxmin, garea, cw, int(ch.sum()),
        '  <== PANEL' if i in (22,40) else ('  <== FIRE' if i==45 else '')))
