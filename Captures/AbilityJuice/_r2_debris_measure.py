import numpy as np
from PIL import Image
import os, json

ROOT = os.path.dirname(os.path.abspath(__file__))
R2 = os.path.join(ROOT, 'shots/r2/debris')
R1 = os.path.join(ROOT, 'shots/r1/debris')

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.float32)

def lum(a):
    return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]

def sat(a):
    mx = a.max(-1); mn = a.min(-1)
    return np.where(mx>1e-5, (mx-mn)/np.maximum(mx,1e-5), 0.0)

def plate_for(D):
    frames = sorted(f for f in os.listdir(D) if f.endswith('.jpg'))
    pre = np.stack([load(os.path.join(D,f)) for f in frames[:6]])
    post = np.stack([load(os.path.join(D,f)) for f in frames[-6:]])
    return np.median(np.concatenate([pre,post]), axis=0), frames

plate, frames = plate_for(R2)
pl = lum(plate)
H,W,_ = plate.shape

# ---- grid seam detection: find dark vertical seam lines in the central floor band
band = pl[int(H*0.42):int(H*0.58), :]
colmean = band.mean(0)
# seams are local minima
mins = []
for x in range(3, W-3):
    w = colmean[x-3:x+4]
    if colmean[x] == w.min() and colmean[x] < colmean[max(0,x-12):x+13].mean() - 3:
        mins.append(x)
# collapse runs
seams=[]
for x in mins:
    if seams and x - seams[-1][-1] <= 3: seams[-1].append(x)
    else: seams.append([x])
seams=[int(np.mean(s)) for s in seams]
print("vertical seams x:", seams)
if len(seams)>=2:
    d = np.diff(seams)
    print("seam spacing:", d, " median cell px =", np.median(d))
