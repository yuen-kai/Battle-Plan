import numpy as np
from PIL import Image
import os, colorsys

ROOT = os.path.dirname(os.path.abspath(__file__))


def lum(a):
    return 0.2126*a[..., 0]+0.7152*a[..., 1]+0.0722*a[..., 2]


def load(r, i):
    p = os.path.join(ROOT, 'shots', r, 'windup')
    fs = sorted(f for f in os.listdir(p) if f.endswith('.jpg'))
    return np.asarray(Image.open(os.path.join(p, fs[i])).convert('RGB')).astype(np.float32)


# ---------- 1. tint colour ----------
f0 = load('r2', 0); L0 = lum(f0)
print("== TELEGRAPH COLOUR ==")
for i, nm in ((22, 'panel1'), (40, 'panel2')):
    a = load('r2', i); d = lum(a)-L0
    m = (d < -45) & (L0 > 150)
    rgb = a[m].mean(0)
    h, s, v = colorsys.rgb_to_hsv(*(rgb/255.0))
    frgb = f0[m].mean(0)
    fh, fs_, fv = colorsys.rgb_to_hsv(*(frgb/255.0))
    print("  %s tint  RGB %3.0f,%3.0f,%3.0f  hue %3.0f deg  S %.2f  V %.2f   (floor beneath: RGB %3.0f,%3.0f,%3.0f hue %3.0f S %.2f V %.2f)"
          % (nm, rgb[0], rgb[1], rgb[2], h*360, s, v, frgb[0], frgb[1], frgb[2], fh*360, fs_, fv))

# ---------- 2. cell snap: do plate edges lie on tile seams? ----------
print("\n== CELL SNAP CHECK ==")
a = load('r2', 40); d = lum(a)-L0
plate = (d < -45) & (L0 > 150)
grad = np.abs(np.gradient(L0)[1])          # clean-plate tile seams
seam = grad > 4
# plate boundary pixels
from scipy import ndimage  # noqa
bnd = plate ^ ndimage.binary_erosion(plate, iterations=2)
# how many boundary px sit within 4 px of a clean-plate seam?
seam_d = ndimage.distance_transform_edt(~seam)
onseam = (seam_d[bnd] <= 4).mean()
print("  %.0f%% of the tint's boundary pixels lie within 4px of a floor tile seam" % (100*onseam))
print("  (a free-form disc would score near the seam density; a cell-snapped plate scores high)")
rand = (seam_d[(L0 > 150)] <= 4).mean()
print("  baseline: %.0f%% of all floor pixels are within 4px of a seam" % (100*rand))

# ---------- 3. verify r1 claim numbers ----------
print("\n== R1 CLAIM RE-CHECK (bright coverage / mark bbox across the r1 strip) ==")
p = os.path.join(ROOT, 'shots', 'r1', 'windup')
fs = sorted(f for f in os.listdir(p) if f.endswith('.jpg'))
print("  r1 frames:", len(fs))
g0 = np.asarray(Image.open(os.path.join(p, fs[0])).convert('RGB')).astype(np.float32)
GL0 = lum(g0)
import json
pk = json.load(open(os.path.join(ROOT, 'strips', 'r1', 'picks.json')))
w = [x for x in pk if x['shot'] == 'windup'][0]
for key in ('windup_frame', 'impact_frame', 'aftermath_frame'):
    i = w[key]
    b = np.asarray(Image.open(os.path.join(p, fs[i])).convert('RGB')).astype(np.float32)
    Lb = lum(b)
    bright = Lb > 200
    mark = np.abs(Lb-GL0) > 12
    ys, xs = np.nonzero(mark)
    print("   %s f%-3d  bright(>200) cov %5.1f%%   mark bbox %dx%d" % (
        key, i, 100*bright.mean(), xs.max()-xs.min(), ys.max()-ys.min()))

# ---------- 4. r2 same metric ----------
print("\n== R2 SAME METRIC ==")
for i, nm in ((22, 'p1 t=+0.33'), (40, 'p2 t=+0.93'), (54, 'p3 t=+1.40')):
    b = load('r2', i); Lb = lum(b)
    bright = Lb > 200
    mark = np.abs(Lb-L0) > 12
    ys, xs = np.nonzero(mark)
    print("   %-12s  bright(>200) cov %5.1f%%   changed-px bbox %dx%d   changed cov %5.1f%%" % (
        nm, 100*bright.mean(), xs.max()-xs.min(), ys.max()-ys.min(), 100*mark.mean()))

# ---------- 5. how long is the telegraph static? ----------
print("\n== STATIC HOLD before the fire ==")
prev = None; first_static = None
for i in range(14, 46):
    b = load('r2', i)
    if prev is not None:
        dd = np.abs(lum(b)-lum(prev)).mean()
        if dd < 0.6 and first_static is None:
            first_static = i
        if dd >= 0.6:
            first_static = None
        print("   f%-3d t=%+.2f  frame-to-frame meanAbsD %5.2f" % (i, i/30.0-0.40, dd))
    prev = b
