import numpy as np, os
from PIL import Image
from scipy import ndimage as ndi
from _c3_db_setup import ROOT, FPS, T0, load, lum, sat, plate_of

CELL = 199.0
for p in ('strips/r3/debris.jpg', 'strips/r2/debris.jpg', 'strips/reference/cm-everyability-20.jpg'):
    print(p, Image.open(os.path.join(ROOT, p)).size)

d3 = os.path.join(ROOT, 'shots/r3/debris')
plate, fr = plate_of(d3); pl = lum(plate)

print()
print('=' * 112)
print('DUST STRUCTURE — local contrast inside the cloud (a pancake has none)')
print('  frame   cloud px   cloud area   local-sigma(9x9)   p10/p50/p90 L   smooth-gradient share')
for i in (13, 14, 15, 16):
    im = load(os.path.join(d3, fr[i])); L = lum(im); S = sat(im)
    m = np.abs(im - plate).max(-1) > 12
    # cloud = the soft grey occluder: mid-dark, low sat, and NOT part of a hard faceted chunk
    # chunks are flat-shaded polygons -> near-zero local variance; find them by very low local sigma
    mean = ndi.uniform_filter(L, 9)
    sq = ndi.uniform_filter(L*L, 9)
    sig = np.sqrt(np.maximum(sq - mean*mean, 0))
    cloud = m & (L > 35) & (L < 140) & (S < 0.30)
    cloud = ndi.binary_erosion(cloud, iterations=3)  # drop boundary pixels vs the bright floor
    if cloud.sum() < 500:
        print('  %+0.3f   (too small)' % ((i-T0)/FPS)); continue
    Lc = L[cloud]
    print('  %+0.3f   %6d     %.2f cell^2      %5.2f            %3.0f/%3.0f/%3.0f          %.0f%% of cloud has sigma<3'
          % ((i-T0)/FPS, int(cloud.sum()), cloud.sum()/CELL**2, float(np.median(sig[cloud])),
             np.percentile(Lc, 10), np.percentile(Lc, 50), np.percentile(Lc, 90),
             100*float((sig[cloud] < 3).mean())))

print()
print('  Same measure on Clash Mini dust/smoke regions (interior of the effect, boundary eroded):')
for nm, path, panel, box in (('CM clash-04 dust', 'strips/reference/cm-clashabilities-04.jpg', 2, (60, 200, 200, 330)),
                             ('CM every-05 plume', 'strips/reference/cm-everyability-05.jpg', 2, (60, 20, 220, 130)),
                             ('CM every-23 bloom', 'strips/reference/cm-everyability-23.jpg', 2, (80, 60, 260, 240)),
                             ('CM 8new-13 blast', 'strips/reference/cm-8newabilities-13.jpg', 1, (60, 60, 260, 260))):
    im = Image.open(os.path.join(ROOT, path)).convert('RGB')
    W, H = im.size; pw = H; step = (W-pw)/2
    a = np.asarray(im.crop((int(panel*step), 0, int(panel*step)+pw, H))).astype(np.float32)
    a = a[box[1]:box[3], box[0]:box[2]]
    if a.size == 0: continue
    L = lum(a)
    mean = ndi.uniform_filter(L, 9); sq = ndi.uniform_filter(L*L, 9)
    sig = np.sqrt(np.maximum(sq-mean*mean, 0))
    inner = np.zeros(L.shape, bool); inner[6:-6, 6:-6] = True
    print('   %-18s local-sigma %5.2f    p10/p50/p90 L %3.0f/%3.0f/%3.0f   %.0f%% sigma<3'
          % (nm, float(np.median(sig[inner])), np.percentile(L[inner], 10), np.percentile(L[inner], 50),
             np.percentile(L[inner], 90), 100*float((sig[inner] < 3).mean())))

print()
print('=' * 112)
print('EDGE HARDNESS of the dust silhouette (law: 2-4 px step, CM 49-91 L/px)')
for i in (14, 15):
    im = load(os.path.join(d3, fr[i])); L = lum(im)
    m = np.abs(im - plate).max(-1) > 12
    cloud = m & (L < 150)
    er = ndi.binary_erosion(cloud, iterations=1)
    edge = cloud & ~er
    gy, gx = np.gradient(L); g = np.hypot(gx, gy)
    v = g[edge]
    print('  %+0.3f  edge |dL/dx| p50 %.0f  p75 %.0f  p90 %.0f  L/px' % ((i-T0)/FPS, *np.percentile(v, [50, 75, 90])))
