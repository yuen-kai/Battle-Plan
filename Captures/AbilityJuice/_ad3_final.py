import numpy as np, os, json
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))
PITCH = 251.0
CROWN = 393

def solid_digits(fr, pl):
    L,S,H = lum(fr), sat(fr), hue(fr)
    ch = np.abs(fr-pl).max(-1) > 20
    ch[int(ch.shape[0]*0.60):,:] = False
    amber = ch & (S>0.40) & (H>25) & (H<75) & (L>90)
    dark  = ch & (L<70)
    solid = ndimage.binary_fill_holes(amber|dark)
    lab,n = ndimage.label(solid)
    if n==0: return None,None
    sizes = ndimage.sum(solid,lab,range(1,n+1))
    solid = lab==int(np.argmax(sizes))+1
    bright = solid & (L>100)
    lab2,n2 = ndimage.label(bright)
    dg = np.zeros_like(bright)
    for k in range(1,n2+1):
        c = lab2==k; a=c.sum()
        if a<300: continue
        ys,xs=np.where(c)
        if a/((xs.max()-xs.min()+1)*(ys.max()-ys.min()+1)) >= 0.30: dg|=c
    return solid, dg

print('=== GAP to victim crown (y=%d), every frame ===' % CROWN)
for rnd in ('r2','r3'):
    pl = plate(rnd,'numbers')
    print(' --',rnd)
    for i in range(12,32):
        fr = load(fp(rnd,'numbers',i))
        s,d = solid_digits(fr,pl)
        if s is None or s.sum()<500: continue
        ys,xs = np.where(s)
        gap = CROWN - ys.max()
        gy,gx = np.where(d)
        print('   f%03d  bottom=%3d gap=%4dpx = %.3f cells | cap=%3dpx=%.3f cells | chromeFrac(ink)=%.3f'
              % (i, ys.max(), gap, gap/PITCH, gy.max()-gy.min()+1, (gy.max()-gy.min()+1)/PITCH,
                 1.0 - d.sum()/s.sum()))

print()
print('=== Clash Mini bare-glyph chrome fraction ===')
def refim(n): return np.asarray(Image.open(os.path.join(ROOT,'strips','reference',n)).convert('RGB')).astype(float)
for name, box in (('cm-everyability-22.jpg',(1200,60,1240,108)),
                  ('cm-everyability-22.jpg',(1265,138,1305,185)),
                  ('cm-everyability-20.jpg',(1275,118,1310,152)),
                  ('cm-everyability-20.jpg',(1338,240,1372,280))):
    im = refim(name); x0,y0,x1,y1 = box
    sub = im[y0:y1,x0:x1]
    L,S = lum(sub), (lambda a:(a.max(-1)-a.min(-1))/np.maximum(a.max(-1),1e-5))(sub)
    ink = (L>140)&(S>0.45)
    key = L<70
    sil = ndimage.binary_fill_holes(ndimage.binary_closing(ink|key, np.ones((3,3))))
    lab,n = ndimage.label(sil)
    if n:
        sizes = ndimage.sum(sil,lab,range(1,n+1)); sil = lab==int(np.argmax(sizes))+1
    ys,xs = np.where(sil)
    print('  %s %s  sil=%4d  ink=%4d  chromeFrac=%.3f  glyph h=%d w=%d'
          % (name[:-4], box, sil.sum(), (ink&sil).sum(), 1-(ink&sil).sum()/max(1,sil.sum()),
             (ys.max()-ys.min()+1) if len(ys) else 0, (xs.max()-xs.min()+1) if len(xs) else 0))
