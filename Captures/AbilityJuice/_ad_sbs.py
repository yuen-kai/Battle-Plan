import numpy as np
from PIL import Image
import os, json

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')

def load(shot, idx):
    p = os.path.join(SH, shot, 'f%04d.jpg' % idx)
    return np.asarray(Image.open(p).convert('RGB')).astype(np.float32)

def lum(a):
    return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]

# probe the grid pitch by autocorrelating the luminance rows/cols of a clean frame
def pitch(shot, clean_idx):
    a = lum(load(shot, clean_idx))
    h, w = a.shape
    # use the horizontal derivative energy summed over the middle band
    band = a[int(h*0.35):int(h*0.65), :]
    d = np.abs(np.diff(band, axis=1)).sum(axis=0)
    d = d - d.mean()
    ac = np.correlate(d, d, mode='full')[len(d)-1:]
    ac[:40] = 0
    return int(np.argmax(ac[:400]))

for shot, clean in (('death', 0), ('pogo', 0)):
    a = load(shot, clean)
    print(shot, 'frame shape', a.shape, 'pitch px', pitch(shot, clean))
    L = lum(a)
    print('  floor lum median', np.median(L), 'p10', np.percentile(L,10), 'p90', np.percentile(L,90))
