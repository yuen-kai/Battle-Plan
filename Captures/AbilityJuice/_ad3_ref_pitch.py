import numpy as np, os
from PIL import Image
ROOT = os.path.dirname(os.path.abspath(__file__))
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def ref(name): return np.asarray(Image.open(os.path.join(ROOT,'strips','reference',name)).convert('RGB')).astype(float)

def profile(name, panel, y0f, y1f):
    im = ref(name); H,W,_ = im.shape; pw = W//3
    x0, x1 = panel*pw, (panel+1)*pw
    band = lum(im[int(H*y0f):int(H*y1f), x0:x1])
    p = band.mean(0)
    return p, x0

def edges(p, smooth=5):
    k = np.ones(smooth)/smooth
    ps = np.convolve(p, k, 'same')
    g = np.gradient(ps)
    # peaks of |g|
    out=[]
    for x in range(4,len(g)-4):
        if abs(g[x]) == np.abs(g[max(0,x-4):x+5]).max() and abs(g[x]) > 0.9:
            out.append((x, round(float(g[x]),2)))
    # collapse
    grp=[]
    for x,v in out:
        if grp and x-grp[-1][-1][0] <= 5: grp[-1].append((x,v))
        else: grp.append([(x,v)])
    return [ (int(np.mean([a for a,_ in g_])), round(float(np.mean([b for _,b in g_])),2)) for g_ in grp]

for name, panel, band in [('cm-everyability-27.jpg',0,(0.60,0.80)),
                          ('cm-everyability-27.jpg',0,(0.30,0.45)),
                          ('cm-everyability-22.jpg',0,(0.62,0.82)),
                          ('cm-everyability-20.jpg',0,(0.62,0.82))]:
    p,x0 = profile(name,panel,*band)
    e = edges(p)
    xs=[a for a,_ in e]
    print(name, band, 'edges', xs, 'diffs', [xs[i+1]-xs[i] for i in range(len(xs)-1)])
