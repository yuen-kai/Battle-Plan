import numpy as np, os, json
from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.float64)

def lum(a):
    return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]

def frames(rnd, shot):
    d = os.path.join(ROOT, 'shots', rnd, shot)
    return [os.path.join(d, f) for f in sorted(os.listdir(d)) if f.endswith('.jpg')]

def plate(rnd, shot, n=6):
    fs = frames(rnd, shot)
    pre = np.stack([load(p) for p in fs[:n]])
    post = np.stack([load(p) for p in fs[-n:]])
    return np.median(np.concatenate([pre, post]), axis=0)

def diffmask(fr, pl, thr=18):
    d = np.abs(fr - pl).max(-1)
    return d > thr

def bbox(m):
    ys, xs = np.where(m)
    if len(ys) == 0: return None
    return xs.min(), ys.min(), xs.max(), ys.max()

if __name__ == '__main__':
    for rnd in ('r2','r3'):
        fs = frames(rnd, 'numbers')
        pl = plate(rnd, 'numbers')
        print('===', rnd, len(fs), 'frames')
        for i, p in enumerate(fs):
            fr = load(p)
            m = diffmask(fr, pl)
            # restrict to upper 60% where the badge lives (avoid unit/health bar churn)
            m2 = m.copy(); m2[int(m.shape[0]*0.55):, :] = False
            n = m2.sum()
            bb = bbox(m2)
            if n > 200:
                print(' f%03d t=%+.3f  px=%5d bbox=%s' % (i, (i-12)/30.0, n, bb))
