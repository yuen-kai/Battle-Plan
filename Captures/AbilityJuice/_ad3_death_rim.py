import numpy as np, os
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

def analyse(rnd, shot, idx, box):
    fr = load(fp(rnd,shot,idx)); x0,y0,x1,y1 = box
    sub = fr[y0:y1, x0:x1]
    L,S,H = lum(sub), sat(sub), hue(sub)
    hot  = (S>0.45)&(L>90)&(((H>=0)&(H<70))|(H>340))        # rim (red/amber)
    dark = L<70
    solid = ndimage.binary_fill_holes(ndimage.binary_closing(hot|dark, np.ones((3,3))))
    lab,n = ndimage.label(solid); sz = ndimage.sum(solid,lab,range(1,n+1))
    solid = lab==int(np.argmax(sz))+1
    bright = solid & (L>140)
    lab2,n2 = ndimage.label(bright)
    rim = np.zeros_like(bright); dg = np.zeros_like(bright)
    for k in range(1,n2+1):
        c = lab2==k; a=c.sum()
        if a<300: continue
        ys,xs=np.where(c); f=a/((xs.max()-xs.min()+1)*(ys.max()-ys.min()+1))
        (rim if f<0.30 else dg).__ior__(c)
    ys,xs=np.where(solid)
    sep = ndimage.distance_transform_edt(~rim)[dg] if rim.any() and dg.any() else np.array([99.])
    print('%s f%03d  badge %dx%d  solid=%d  digitInk=%d chromeFrac=%.3f  digit->rim min=%.0f p1=%.0f median=%.0f'
          % (shot, idx, xs.max()-xs.min()+1, ys.max()-ys.min()+1, solid.sum(), dg.sum(),
             1-dg.sum()/solid.sum(), sep.min(), np.percentile(sep,1), np.median(sep)))
    gy,gx = np.where(dg)
    if len(gy): print('        cap=%dpx  glyphbox=%dx%d' % (gy.max()-gy.min()+1, gx.max()-gx.min()+1, gy.max()-gy.min()+1))
    vis = (sub*0.35).astype(np.uint8); vis[rim]=(60,255,60); vis[dg]=(255,120,0)
    Image.fromarray(vis).resize((sub.shape[1]*2, sub.shape[0]*2), Image.NEAREST)\
         .save(os.path.join(ROOT,'_ad3_%s_%03d_seg.png'%(shot,idx)))

analyse('r3','death',13,(440,270,890,420))
analyse('r3','death',26,(600,150,900,300))
