import numpy as np, os
from PIL import Image
from scipy import ndimage as ndi
from _c3_db_setup import ROOT, FPS, T0, load, lum, sat, plate_of

CELL = 199.0
d3 = os.path.join(ROOT, 'shots/r3/debris')
d2 = os.path.join(ROOT, 'shots/r2/debris')

print('=' * 112)
print('DUST MASS — solid pancake, or a mass with structure?')
print('   round   t      bbox(cells)   union-fill%   hull-fill%   holes%   internal L-spread   L p10/p50/p90   edge steepness')
for nm, d, idxs in (('R2', d2, (14, 15, 17)), ('R3', d3, (14, 15, 17))):
    plate, fr = plate_of(d); pl = lum(plate)
    for i in idxs:
        im = load(os.path.join(d, fr[i])); L = lum(im); S = sat(im)
        m = np.abs(im - plate).max(-1) > 12
        if m.sum() < 500: continue
        y, x = np.nonzero(m)
        bw, bh = x.max()-x.min()+1, y.max()-y.min()+1
        box = m[y.min():y.max()+1, x.min():x.max()+1]
        fill = box.mean()
        filled = ndi.binary_fill_holes(box)
        holes = (filled & ~box).mean()
        # convex hull fill
        try:
            from scipy.spatial import ConvexHull
            pts = np.column_stack(np.nonzero(box))[::7]
            hull = ConvexHull(pts)
            hull_fill = box.sum()/hull.volume
        except Exception:
            hull_fill = float('nan')
        # dust = greyish occluding material, excluding hot pixels
        dust = m & (L < 150) & (S < 0.35)
        Ld = L[dust]
        spread = float(np.percentile(Ld, 90) - np.percentile(Ld, 10)) if dust.sum() > 500 else float('nan')
        # edge steepness: |grad L| along the outer boundary of the dust
        er = ndi.binary_erosion(dust, iterations=2)
        edge = dust & ~er
        gy, gx = np.gradient(L)
        g = np.hypot(gx, gy)
        steep = float(np.percentile(g[edge], 75)) if edge.sum() > 200 else float('nan')
        print('   %s   %+0.3f   %.2f x %.2f      %4.1f        %4.1f      %4.1f        %5.1f         %3.0f/%3.0f/%3.0f      %.1f L/px'
              % (nm, (i-T0)/FPS, bw/CELL, bh/CELL, 100*fill, 100*hull_fill, 100*holes, spread,
                 np.percentile(Ld, 10), np.percentile(Ld, 50), np.percentile(Ld, 90), steep))

print()
print('=' * 112)
print('SQUINT — panel coverage at 1/9 scale (r2 critic method), ours vs Clash Mini')
def squint_cov(path, panel, npan=3):
    im = Image.open(os.path.join(ROOT, path)).convert('RGB')
    W, H = im.size
    pw = H  # panels are square, separated by thin bars
    step = (W - pw) / (npan - 1)
    p = im.crop((int(panel*step), 0, int(panel*step)+pw, H)).resize((57, 57), Image.LANCZOS)
    a = np.asarray(p).astype(np.float32); L = lum(a); S = sat(a)
    bgL = np.median(L); bgS = np.median(S)
    ev = (np.abs(L-bgL) > 28) | (np.abs(S-bgS) > 0.18)
    return 100*ev.mean(), bgL, float(np.percentile(L, 99)-np.percentile(L, 1))
for nm, p in (('OURS r3 debris', 'strips/r3/debris.jpg'),
              ('OURS r2 debris', 'strips/r2/debris.jpg'),
              ('CM clashabilities-04', 'strips/reference/cm-clashabilities-04.jpg'),
              ('CM everyability-20', 'strips/reference/cm-everyability-20.jpg'),
              ('CM everyability-23', 'strips/reference/cm-everyability-23.jpg'),
              ('CM 8newabilities-13', 'strips/reference/cm-8newabilities-13.jpg'),
              ('CM everyability-05', 'strips/reference/cm-everyability-05.jpg'),
              ('CM everyability-27', 'strips/reference/cm-everyability-27.jpg')):
    c1, _, _ = squint_cov(p, 0); c2, bg, rng = squint_cov(p, 1); c3, _, _ = squint_cov(p, 2)
    print('  %-22s  windup %4.1f%%   IMPACT %4.1f%%   AFTERMATH %4.1f%%    (aftermath/impact %.2f)'
          % (nm, c1, c2, c3, c3/max(c2, 1e-6)))

print()
print('=' * 112)
print('BRIGHT-AND-SATURATED fraction of the panel (Clash Mini carries 1-5%)')
def bs_frac(path, panel, npan=3):
    im = Image.open(os.path.join(ROOT, path)).convert('RGB')
    W, H = im.size; pw = H; step = (W-pw)/(npan-1)
    a = np.asarray(im.crop((int(panel*step), 0, int(panel*step)+pw, H))).astype(np.float32)
    L = lum(a); S = sat(a)
    return 100*((L > 150) & (S > 0.45)).mean()
for nm, p in (('OURS r3 debris', 'strips/r3/debris.jpg'),
              ('OURS r2 debris', 'strips/r2/debris.jpg'),
              ('CM clashabilities-04', 'strips/reference/cm-clashabilities-04.jpg'),
              ('CM everyability-20', 'strips/reference/cm-everyability-20.jpg'),
              ('CM everyability-23', 'strips/reference/cm-everyability-23.jpg'),
              ('CM 8newabilities-13', 'strips/reference/cm-8newabilities-13.jpg')):
    print('  %-22s  windup %5.2f%%   IMPACT %5.2f%%   AFTERMATH %5.2f%%' % (nm, bs_frac(p, 0), bs_frac(p, 1), bs_frac(p, 2)))
