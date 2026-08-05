import numpy as np, os, json
from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))

def load(p):
    return np.asarray(Image.open(os.path.join(ROOT, p)).convert('RGB')).astype(np.float64)

def lum(a):
    return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]

def sat(a):
    mx = a.max(-1); mn = a.min(-1)
    return np.where(mx > 1e-5, (mx-mn)/np.maximum(mx, 1e-5), 0.0)

def frames(rnd, shot):
    d = os.path.join(ROOT, 'shots', rnd, shot)
    return [os.path.join(d, f) for f in sorted(os.listdir(d)) if f.endswith('.jpg')]

def plate(rnd, shot, n=6):
    fs = frames(rnd, shot)
    pre = np.stack([np.asarray(Image.open(p).convert('RGB')).astype(np.float64) for p in fs[:n]])
    post = np.stack([np.asarray(Image.open(p).convert('RGB')).astype(np.float64) for p in fs[-n:]])
    return np.median(np.concatenate([pre, post]), axis=0)

def seams(pl, y0=0.42, y1=0.58, thr=3):
    H, W = pl.shape
    band = pl[int(H*y0):int(H*y1), :]
    cm = band.mean(0)
    mins = []
    for x in range(3, W-3):
        if cm[x] == cm[x-3:x+4].min() and cm[x] < cm[max(0,x-12):x+13].mean() - thr:
            mins.append(x)
    grp = []
    for x in mins:
        if grp and x - grp[-1][-1] <= 3:
            grp[-1].append(x)
        else:
            grp.append([x])
    return [float(np.mean(g)) for g in grp]

if __name__ == '__main__':
    for rnd in ('r2','r3'):
        for shot in ('numbers','death','pogo'):
            try:
                pl = plate(rnd, shot)
            except Exception as e:
                print(rnd, shot, 'MISSING', e); continue
            L = lum(pl)
            for band in [(0.30,0.40),(0.42,0.58),(0.62,0.74),(0.78,0.90)]:
                s = seams(L, *band)
                if len(s) > 1:
                    print(rnd, shot, 'band', band, 'seams', [round(v,1) for v in s],
                          'pitch', [round(s[i+1]-s[i],1) for i in range(len(s)-1)])
            print(rnd, shot, 'nframes', len(frames(rnd, shot)), 'floor p50 %.1f' % np.median(L))
            print()
