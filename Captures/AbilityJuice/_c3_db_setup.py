import numpy as np
from PIL import Image
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
FPS = 30.0
T0 = 12  # frame index of t=0.000 (picks.json: frame 11 = -0.0333, frame 15 = +0.100)

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.float32)

def lum(a):
    return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]

def sat(a):
    mx = a.max(-1); mn = a.min(-1)
    return np.where(mx > 1e-5, (mx-mn)/np.maximum(mx, 1e-5), 0.0)

def frames_of(d):
    return sorted(f for f in os.listdir(d) if f.endswith('.jpg'))

def plate_of(d):
    fr = frames_of(d)
    pre = np.stack([load(os.path.join(d, f)) for f in fr[:6]])
    post = np.stack([load(os.path.join(d, f)) for f in fr[-6:]])
    return np.median(np.concatenate([pre, post]), axis=0), fr

def seam_pitch(pl):
    H, W = pl.shape
    band = pl[int(H*0.42):int(H*0.58), :]
    colmean = band.mean(0)
    mins = []
    for x in range(3, W-3):
        w = colmean[x-3:x+4]
        if colmean[x] == w.min() and colmean[x] < colmean[max(0, x-12):x+13].mean() - 3:
            mins.append(x)
    seams = []
    for x in mins:
        if seams and x - seams[-1][-1] <= 3:
            seams[-1].append(x)
        else:
            seams.append([x])
    seams = [int(np.mean(s)) for s in seams]
    return seams

if __name__ == '__main__':
    for rnd in ('r2', 'r3'):
        d = os.path.join(ROOT, 'shots', rnd, 'debris')
        plate, fr = plate_of(d)
        pl = lum(plate)
        s = seam_pitch(pl)
        print(rnd, 'frames', len(fr), 'plate floor lum p50=%.1f' % np.median(pl),
              'seams', s, 'spacing', np.diff(s) if len(s) > 1 else '-')
