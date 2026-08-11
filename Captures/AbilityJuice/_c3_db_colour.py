import numpy as np, os
from PIL import Image
from scipy import ndimage as ndi
from _c3_db_setup import ROOT, FPS, T0, load, lum, sat, plate_of

CELL = 199.0

def panel(path, k, n=3):
    im = Image.open(os.path.join(ROOT, path)).convert('RGB')
    W, H = im.size; pw = H; step = (W-pw)/(n-1)
    return np.asarray(im.crop((int(k*step), 0, int(k*step)+pw, H))).astype(np.float32)

def cm_cell_px(a):
    """checker pitch from the板 pattern: autocorrelation of a mid row band."""
    L = lum(a)
    row = L[int(a.shape[0]*0.75)] - L[int(a.shape[0]*0.75)].mean()
    ac = np.correlate(row, row, 'full')[len(row)-1:]
    ac /= ac[0]
    peaks = [k for k in range(20, 160) if ac[k] == max(ac[max(20, k-6):k+7]) and ac[k] > 0.15]
    return peaks[0] if peaks else None

print('=' * 112)
print('COLOURED-MATERIAL AREA — how much of the frame is actually saturated colour, in cells^2')
print('  (S>=0.50 and L>=120: material a viewer reads as "hot / coloured", not grey)')

d3 = os.path.join(ROOT, 'shots/r3/debris')
plate, fr = plate_of(d3)
print('\n  OURS r3 debris (full 1024 capture, cell = 199 px):')
for i in (12, 13, 14, 15, 16):
    im = load(os.path.join(d3, fr[i])); L = lum(im); S = sat(im)
    m = np.abs(im - plate).max(-1) > 12
    col = m & (S >= 0.50) & (L >= 120)
    grey = m & (S < 0.30) & (L < 150)
    lab, n = ndi.label(col)
    sz = ndi.sum(col, lab, range(1, n+1)) if n else np.array([])
    big = sz[sz >= 200]
    print('   %+0.3f  coloured %.3f cell^2 (%5d px)   grey occluder %.3f cell^2   grey:colour %4.0f:1   %d coloured blobs>=200px, largest %.3f cell^2'
          % ((i-T0)/FPS, col.sum()/CELL**2, int(col.sum()), grey.sum()/CELL**2,
             grey.sum()/max(col.sum(), 1), len(big), (big.max()/CELL**2) if len(big) else 0))

print('\n  Clash Mini reference panels (cell pitch measured from the board checker):')
for nm, path in (('everyability-20', 'strips/reference/cm-everyability-20.jpg'),
                 ('everyability-23', 'strips/reference/cm-everyability-23.jpg'),
                 ('clashabilities-04', 'strips/reference/cm-clashabilities-04.jpg'),
                 ('8newabilities-13', 'strips/reference/cm-8newabilities-13.jpg')):
    a0 = panel(path, 0)
    cp = cm_cell_px(a0)
    if not cp:
        cp = 85
        note = '(assumed 85px)'
    else:
        note = ''
    print('   %-18s cell=%3dpx %s' % (nm, cp, note), end='')
    for k, lbl in ((1, 'IMPACT'), (2, 'AFTER')):
        a = panel(path, k); L = lum(a); S = sat(a)
        col = (S >= 0.50) & (L >= 120)
        print('   %s %.2f cell^2 (%.1f%% of panel)' % (lbl, col.sum()/cp**2, 100*col.mean()), end='')
    print()

print()
print('=' * 112)
print('OURS, same units: coloured area in the SHIPPED strip panels')
for nm, p in (('r3 debris', 'strips/r3/debris.jpg'), ('r2 debris', 'strips/r2/debris.jpg')):
    # strip panel is 512px tall; capture is 1024 -> panel crop is 512/1024 of a frame? measure from tile seams
    a1 = panel(p, 1); L1 = lum(a1); S1 = sat(a1)
    a2 = panel(p, 2); L2 = lum(a2); S2 = sat(a2)
    c1 = (S1 >= 0.50) & (L1 >= 120); c2 = (S2 >= 0.50) & (L2 >= 120)
    # cell pitch on the strip panel: seams of the plain floor
    row = L1[int(512*0.85)]
    mins = [x for x in range(6, 506) if row[x] == row[x-5:x+6].min() and row[x] < row[max(0, x-16):x+17].mean()-3]
    sp = np.diff([x for k, x in enumerate(mins) if k == 0 or x-mins[k-1] > 6])
    cp = float(np.median(sp)) if len(sp) else 100.0
    print('  %s  cell=%.0fpx   IMPACT coloured %.3f cell^2 (%.2f%% of panel)   AFTERMATH %.3f cell^2 (%.2f%%)'
          % (nm, cp, c1.sum()/cp**2, 100*c1.mean(), c2.sum()/cp**2, 100*c2.mean()))
