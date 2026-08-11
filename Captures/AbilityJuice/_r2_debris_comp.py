import numpy as np
from PIL import Image
import os, json
from scipy import ndimage as ndi

ROOT = os.path.dirname(os.path.abspath(__file__))
R2 = os.path.join(ROOT, 'shots/r2/debris')
CELL=199.0; FPS=30.0; T0=12
def load(p): return np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]

frames = sorted(f for f in os.listdir(R2) if f.endswith('.jpg'))
pre=np.stack([load(os.path.join(R2,f)) for f in frames[:6]])
post=np.stack([load(os.path.join(R2,f)) for f in frames[-6:]])
plate=np.median(np.concatenate([pre,post]),axis=0); pl=lum(plate)

# --- 1) match strip panels to source frames
strip = np.asarray(Image.open(os.path.join(ROOT,'strips/r2/debris.jpg')).convert('RGB')).astype(np.float32)
print("strip", strip.shape)
# 3 panels of 512 wide + 2 separators of 8 => 1552 = 512*3 + 8*2
panels=[strip[:, 0:512], strip[:, 520:1032], strip[:,1040:1552]]
for pi,pan in enumerate(panels):
    best=None
    for i,f in enumerate(frames):
        im = load(os.path.join(R2,f))
        small = np.asarray(Image.fromarray(im.astype(np.uint8)).resize((512,512), Image.LANCZOS)).astype(np.float32)
        e = np.abs(small-pan).mean()
        if best is None or e<best[1]: best=(i,e)
    print("panel %d -> frame %d  t=%+.3f  err=%.2f"%(pi,best[0],(best[0]-T0)/FPS,best[1]))
