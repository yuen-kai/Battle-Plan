import numpy as np
from PIL import Image
import os, json
from scipy import ndimage as ndi

ROOT = os.path.dirname(os.path.abspath(__file__))
R2 = os.path.join(ROOT, 'shots/r2/debris')
R1 = os.path.join(ROOT, 'shots/r1/debris')
CELL=199.0; FPS=30.0; T0=12
def load(p): return np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(-1); mn=a.min(-1); return np.where(mx>1e-5,(mx-mn)/np.maximum(mx,1e-5),0.)

def getplate(D):
    fr=sorted(f for f in os.listdir(D) if f.endswith('.jpg'))
    pre=np.stack([load(os.path.join(D,f)) for f in fr[:6]])
    post=np.stack([load(os.path.join(D,f)) for f in fr[-6:]])
    return np.median(np.concatenate([pre,post]),axis=0), fr

plate,frames = getplate(R2); pl=lum(plate)

print("### CHUNK HIERARCHY — solid chunk bodies only (lum<=120, saturated-or-dark, not dust)")
print("  chunks are brown/orange rock; dust puff is desat grey. separate by saturation+value.")
for fi in [12,13,14,15,16,17,19,22]:
    im=load(os.path.join(R2,'f%04d.jpg'%fi)); L=lum(im); S=sat(im)
    d=np.abs(im-plate).max(-1); m=d>12
    # chunk = darker than plate by >70 AND (warm: R>B) -> rock body
    warm = (im[...,0]-im[...,2])>10
    chunk = m & (L<130) & warm
    lab,n = ndi.label(chunk)
    sizes = ndi.sum(chunk, lab, range(1,n+1))
    objs = ndi.find_objects(lab)
    big=[]
    for k,s in enumerate(sizes):
        if s<80: continue
        sl=objs[k]
        w=(sl[1].stop-sl[1].start)/CELL; h=(sl[0].stop-sl[0].start)/CELL
        big.append((max(w,h), w,h, int(s), sl[1].start, sl[0].start))
    big.sort(reverse=True)
    print(" t=%+0.3f  n_components(>=80px)=%d   top sizes (max dim, cells): %s"%(
        (fi-T0)/FPS, len(big), ", ".join("%.2f"%b[0] for b in big[:12])))
    if big:
        dims=[b[0] for b in big]
        print("        largest %.2f  median %.2f  ratio %.1f:1   count>=0.45c: %d  0.20-0.35c: %d  <0.15c: %d"%(
            dims[0], float(np.median(dims)), dims[0]/max(float(np.median(dims)),1e-3),
            sum(1 for x in dims if x>=0.45), sum(1 for x in dims if 0.20<=x<=0.35), sum(1 for x in dims if x<0.15)))

print()
print("### PALETTE — chunk body luminance & hue vs floor(186)")
for fi in [12,14,16,19,22,25]:
    im=load(os.path.join(R2,'f%04d.jpg'%fi)); L=lum(im); S=sat(im)
    d=np.abs(im-plate).max(-1); m=d>12
    warm=(im[...,0]-im[...,2])>10
    chunk=m&(L<150)&warm
    dust = m&(~warm)&(L<170)
    if chunk.sum()>200:
        c=im[chunk]; cl=L[chunk]
        print(" t=%+0.3f CHUNK n=%6d  lum p10/p50/p90 = %5.1f/%5.1f/%5.1f  RGB med (%3.0f,%3.0f,%3.0f)  R-B med %+.0f  sat p50 %.2f"%(
            (fi-T0)/FPS, chunk.sum(), np.percentile(cl,10),np.percentile(cl,50),np.percentile(cl,90),
            np.median(c[:,0]),np.median(c[:,1]),np.median(c[:,2]), np.median(c[:,0]-c[:,2]), np.median(S[chunk])))
    if dust.sum()>200:
        c=im[dust]; cl=L[dust]
        print("            DUST  n=%6d  lum p10/p50/p90 = %5.1f/%5.1f/%5.1f  RGB med (%3.0f,%3.0f,%3.0f)  R-B med %+.0f  internal sd %.1f"%(
            dust.sum(), np.percentile(cl,10),np.percentile(cl,50),np.percentile(cl,90),
            np.median(c[:,0]),np.median(c[:,1]),np.median(c[:,2]), np.median(c[:,0]-c[:,2]), cl.std()))

print()
print("### BLUE CHECK — any pixel with B-R > 15 inside the effect (team blue leakage)")
for fi in [12,14,16,19,22]:
    im=load(os.path.join(R2,'f%04d.jpg'%fi))
    d=np.abs(im-plate).max(-1); m=d>12
    blue = m & ((im[...,2]-im[...,0])>15)
    print("  t=%+0.3f  blue px = %d  (%.2f%% of effect)"%((fi-T0)/FPS, blue.sum(), 100*blue.sum()/max(m.sum(),1)))
