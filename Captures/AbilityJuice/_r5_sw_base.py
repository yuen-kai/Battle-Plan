"""Round 5 baseline: measure the SHIPPED r4 shockwave frames the way the critic did.

Everything printed here is MEASURED on real captured frames (shots/r4/shockwave/*.jpg),
downsampled 1024 -> 512 exactly as the strip builder does, so a percentage is a percentage
of the 512x512 strip panel.
"""
import numpy as np
import os
import sys
from PIL import Image

ROOT = os.path.dirname(os.path.abspath(__file__))
SHOT = os.path.join(ROOT, 'shots/r4/shockwave')
PANEL = 512


def panel(idx):
    im = Image.open(os.path.join(SHOT, 'f%04d.jpg' % idx)).convert('RGB')
    return np.asarray(im.resize((PANEL, PANEL), Image.LANCZOS), dtype=np.float64)


def lum(px):
    return 0.2126 * px[..., 0] + 0.7152 * px[..., 1] + 0.0722 * px[..., 2]


def sat(px):
    mx, mn = px.max(-1), px.min(-1)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


plate = panel(2)          # long before the spawn: the clean deck
Lp = lum(plate)
print('deck plate: L mean %.1f  p5 %.0f p95 %.0f' % (Lp.mean(), np.percentile(Lp, 5), np.percentile(Lp, 95)))

print('\n=== MEASURED on r4 frames, per 512x512 panel ===')
print('frame   t       >L240    >=250 all3   Lmax   mass(px)  centreL')
for idx in (13, 14, 15, 16, 17, 18, 19, 21, 30):
    px = panel(idx)
    L = lum(px)
    changed = np.abs(px - plate).max(-1) > 20
    hot = L > 240
    clip3 = (px >= 250).all(-1)
    mass = changed & (L < Lp - 25)
    # the middle of the blast: a 60px disc on the centroid of the whole changed footprint
    ys, xs = np.nonzero(changed)
    cy, cx = (ys.mean(), xs.mean()) if len(ys) else (256, 256)
    yy, xx = np.mgrid[0:PANEL, 0:PANEL]
    mid = (yy - cy) ** 2 + (xx - cx) ** 2 < 30 ** 2
    print('f%02d  %+.4f  %6.3f%%  %8.3f%%   %5.1f  %7d   %6.1f'
          % (idx, (idx - 12) / 30.0, 100.0 * hot.mean(), 100.0 * clip3.mean(),
             L.max(), int(mass.sum()), L[mid].mean()))

# --- interior gradient, the critic's 27.1 levels/px ---
px = panel(17)
L = lum(px)
changed = np.abs(px - plate).max(-1) > 20
mass = changed & (L < Lp - 25)
try:
    from scipy.ndimage import binary_erosion
    interior = binary_erosion(mass, np.ones((9, 9)))
except ImportError:
    interior = mass
gy, gx = np.gradient(L)
grad = np.hypot(gy, gx)
g = grad[interior]
print('\nf17 interior mask %d px' % interior.sum())
print('  |grad L| mean %.2f  p50 %.2f  p90 %.2f  p99 %.2f  max %.1f'
      % (g.mean(), np.percentile(g, 50), np.percentile(g, 90), np.percentile(g, 99), g.max()))
li = L[interior]
print('  interior L: mean %.1f std %.2f  p5 %.0f p95 %.0f spread %.0f'
      % (li.mean(), li.std(), np.percentile(li, 5), np.percentile(li, 95),
         np.percentile(li, 95) - np.percentile(li, 5)))

# --- cell pitch: the deck's tile seams in the clean plate ---
col = Lp.mean(axis=0)
d = np.abs(np.diff(col))
peaks = [i for i in range(2, PANEL - 3) if d[i] > 3.0 and d[i] == d[i - 2:i + 3].max()]
print('\ntile seam columns in the clean plate:', peaks)
if len(peaks) > 1:
    gaps = np.diff(peaks)
    print('  gaps:', gaps, ' -> cell pitch on the panel ~%.1f px' % np.median(gaps[gaps > 20]))

# --- reference: what Clash Mini impact panels do ---
REF = os.path.join(ROOT, 'reference/impact')
if os.path.isdir(REF):
    print('\n=== MEASURED on Clash Mini impact references ===')
    for name in sorted(os.listdir(REF))[:14]:
        if not name.lower().endswith(('.jpg', '.png')):
            continue
        im = Image.open(os.path.join(REF, name)).convert('RGB')
        a = np.asarray(im, dtype=np.float64)
        L = lum(a)
        print('  %-38s %6.2f%% >L240   %5.2f%% >=250all3  Lmax %3.0f'
              % (name, 100.0 * (L > 240).mean(), 100.0 * (a >= 250).all(-1).mean(), L.max()))
