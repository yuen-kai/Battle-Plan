import numpy as np
from PIL import Image
import sys, os

ROOT = os.path.dirname(os.path.abspath(__file__))

def load(p):
    return np.asarray(Image.open(os.path.join(ROOT, p)).convert('RGB')).astype(np.float64)

def lum(a):
    return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]

def panels(img, n=3):
    """Split a strip into n panels, detecting the black divider columns."""
    h, w, _ = img.shape
    L = lum(img)
    colmean = L.mean(axis=0)
    dark = colmean < 25
    # find runs of dark columns
    runs = []
    i = 0
    while i < w:
        if dark[i]:
            j = i
            while j < w and dark[j]:
                j += 1
            runs.append((i, j))
            i = j
        else:
            i += 1
    runs = [r for r in runs if r[1]-r[0] >= 3]
    bounds = [0]
    for a,b in runs:
        bounds.append(a); bounds.append(b)
    bounds.append(w)
    segs = [(bounds[k], bounds[k+1]) for k in range(0, len(bounds), 2)]
    segs = [s for s in segs if s[1]-s[0] > 50]
    return segs, runs

if __name__ == '__main__':
    for rel in ['strips/r3/numbers.jpg', 'strips/r2/numbers.jpg']:
        img = load(rel)
        segs, runs = panels(img)
        print(rel, img.shape, 'panels', segs, 'dividers', runs)
