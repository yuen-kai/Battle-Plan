import numpy as np
from PIL import Image
import os, colorsys

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')
PITCH = {'death': 218.0, 'pogo': 158.0}
CEN = {'death': (518, 470), 'pogo': (770, 500)}


def load(shot, i):
    return np.asarray(Image.open(os.path.join(SH, shot, 'f%04d.jpg' % i)).convert('RGB')).astype(np.float32)


def lum(a):
    return 0.2126*a[..., 0]+0.7152*a[..., 1]+0.0722*a[..., 2]


def roi(shot, a, cells):
    p = PITCH[shot]; cx, cy = CEN[shot]; h = int(cells*p/2)
    H, W = a.shape[:2]
    return a[max(0, cy-h):min(H, cy+h), max(0, cx-h):min(W, cx+h)]


def hsv_stats(rgb, mask):
    px = rgb[mask]/255.0
    if len(px) == 0:
        return None
    mx = px.max(1); mn = px.min(1)
    v = mx; s = np.where(mx > 1e-4, (mx-mn)/np.maximum(mx, 1e-4), 0)
    r, g, b = px[:, 0], px[:, 1], px[:, 2]
    d = np.maximum(mx-mn, 1e-6)
    h = np.zeros_like(mx)
    im_ = (mx == r); h[im_] = ((g-b)[im_]/d[im_]) % 6
    im_ = (mx == g) & ~(mx == r); h[im_] = ((b-r)[im_]/d[im_])+2
    im_ = (mx == b) & ~(mx == r) & ~(mx == g); h[im_] = ((r-g)[im_]/d[im_])+4
    h = h*60
    return dict(n=len(px), hue_med=float(np.median(h[s > 0.12])) if (s > 0.12).sum() else float('nan'),
                sat_med=float(np.median(s)), val_med=float(np.median(v)*255),
                sat_p90=float(np.percentile(s, 90)))


print('=== dominant opaque mass: colour + size ===')
for shot, frames, cells in (('death', [13, 14, 16, 20, 26], 3.0), ('pogo', [45, 47, 48, 50, 58], 3.5)):
    p = PITCH[shot]
    clean = load(shot, 0)
    for f in frames:
        a = load(shot, f)
        r = roi(shot, a, cells); rc = roi(shot, clean, cells)
        L = lum(r); Lc = lum(rc)
        changed = np.abs(r-rc).max(-1) > 26
        # opaque material only (excludes the unit's own dark outline via 'changed')
        mass = changed & (L < 110)
        st = hsv_stats(r, mass)
        area = mass.sum()/(p*p)
        # hot pixels
        hot = changed & (L > 170)
        hs = hsv_stats(r, hot)
        clip1 = ((r >= 250).sum(-1) >= 1) & changed
        clipall = ((r >= 250).all(-1)) & changed
        print('%-6s f%-3d mass %.2f cells^2  medL %5.1f  medS %.2f  hue %6.1f | hot n=%5d medS %.2f hue %6.1f | clip1 %5d clipall %5d' % (
            shot, f, area, st['val_med'], st['sat_med'], st['hue_med'],
            hs['n'] if hs else 0, hs['sat_med'] if hs else 0, hs['hue_med'] if hs else float('nan'),
            clip1.sum(), clipall.sum()))

print()
print('=== occlusion check: tile seam contrast under the mass ===')
for shot, f, cells in (('death', 13, 1.2), ('death', 26, 1.2), ('pogo', 48, 1.2), ('pogo', 58, 1.2)):
    a = load(shot, f); clean = load(shot, 0)
    r = lum(roi(shot, a, cells)); rc = lum(roi(shot, clean, cells))
    # seam contrast = local gradient energy
    g = np.abs(np.diff(r, axis=1)); gc = np.abs(np.diff(rc, axis=1))
    print('%-6s f%-3d  seam grad p99 clean %.1f -> effect %.1f   medL clean %.1f -> %.1f' % (
        shot, f, np.percentile(gc, 99), np.percentile(g, 99), np.median(rc), np.median(r)))
