import numpy as np, os
from _c3_db_setup import ROOT, FPS, T0, load, lum, sat, plate_of

CELL = 199.0
d = os.path.join(ROOT, 'shots/r3/debris')
plate, fr = plate_of(d)
pl = lum(plate)

print('=' * 112)
print('SATURATION — five defensible definitions, r3 debris.  Ask was "saturation >= 0.60" on the money frame.')
print('   t      satHot_p50  satHot_p90  satWARM_p50  sat@clip_p50  sat_of_top1%%L   #warm>0.45&L>150  frac of effect')
for i in (12, 13, 14, 15, 16, 17):
    im = load(os.path.join(d, fr[i])); L = lum(im); S = sat(im)
    m = np.abs(im - plate).max(-1) > 12
    hot = m & (L > 150)
    warm = m & (L > 150) & ((im[..., 0] - im[..., 2]) > 30)
    clipm = ((im >= 250).all(-1) & m)
    topL = m & (L >= np.percentile(L[m], 99))
    bs = m & (L > 150) & (S > 0.45)
    def p(mask, q):
        return float(np.percentile(S[mask], q)) if mask.sum() > 30 else float('nan')
    print('  %+0.3f    %.3f       %.3f       %.3f        %.3f         %.3f          %6d          %.2f%%'
          % ((i-T0)/FPS, p(hot, 50), p(hot, 90), p(warm, 50), p(clipm, 50), p(topL, 50),
             int(bs.sum()), 100.0*bs.sum()/max(m.sum(), 1)))

print()
print('=' * 112)
print('BLEACHING LAW — are the clipped pixels white, or do they clip in ONE channel and keep hue?')
print('   t    clip(all3)  clipR_only  clipRG  meanRGB@clip           meanRGB@warmhot        hue_read')
for i in (12, 13, 14, 15, 16):
    im = load(os.path.join(d, fr[i])); L = lum(im)
    m = np.abs(im - plate).max(-1) > 12
    c_all = (im >= 250).all(-1) & m
    c_r = (im[..., 0] >= 250) & (im[..., 1] < 250) & m
    c_rg = (im[..., 0] >= 250) & (im[..., 1] >= 250) & (im[..., 2] < 250) & m
    warmhot = m & (L > 150) & ((im[..., 0] - im[..., 2]) > 60)
    a = im[c_all].mean(0) if c_all.sum() else np.zeros(3)
    b = im[warmhot].mean(0) if warmhot.sum() > 30 else np.zeros(3)
    print('  %+0.3f   %6d     %6d     %6d   (%3.0f,%3.0f,%3.0f)          (%3.0f,%3.0f,%3.0f)        %s'
          % ((i-T0)/FPS, int(c_all.sum()), int(c_r.sum()), int(c_rg.sum()), a[0], a[1], a[2], b[0], b[1], b[2],
             'white core' if a[2] > 200 else 'coloured'))

print()
print('=' * 112)
print('OCCLUSION — tile-seam contrast under the material (clean floor seam contrast ~39; opaque must be < 8)')
# find seam columns from the plate, then sample seam vs neighbour inside/outside the effect
band = pl[400:700, :]
colmean = band.mean(0)
seams = [x for x in range(6, 1018) if colmean[x] == colmean[x-5:x+6].min()
         and colmean[x] < colmean[max(0, x-14):x+15].mean() - 3]
seamcols = []
for x in seams:
    if seamcols and x - seamcols[-1][-1] <= 3: seamcols[-1].append(x)
    else: seamcols.append([x])
seamcols = [int(np.mean(s)) for s in seamcols]
for i in (14, 15, 16):
    im = load(os.path.join(d, fr[i])); L = lum(im)
    m = np.abs(im - plate).max(-1) > 12
    covered, clean = [], []
    for x in seamcols:
        if x < 20 or x > 1000: continue
        for y in range(200, 900, 4):
            c = abs(float(pl[y, x]) - float(pl[y, max(0, x-14)]))
            if c < 20: continue
            obs = abs(float(L[y, x]) - float(L[y, max(0, x-14)]))
            if m[y, x] and m[y, max(0, x-14)] and (pl[y, x] - L[y, x]) > 60: covered.append(obs)
            elif not m[y, x]: clean.append(obs)
    print('  %+0.3f  seam contrast: clean n=%d median %.1f  |  UNDER opaque material n=%d median %.1f  -> %s'
          % ((i-T0)/FPS, len(clean), np.median(clean) if clean else -1,
             len(covered), np.median(covered) if covered else -1,
             'OCCLUDES' if covered and np.median(covered) < 8 else ('translucent' if covered else 'n/a')))
