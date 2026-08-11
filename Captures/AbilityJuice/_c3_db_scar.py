import numpy as np, os
from PIL import Image
from _c3_db_setup import ROOT, FPS, T0, load, lum, sat, plate_of

CELL = 199.0
d3 = os.path.join(ROOT, 'shots/r3/debris')
plate, fr = plate_of(d3); pl = lum(plate)

Image.open(os.path.join(d3, fr[26])).convert('RGB').crop((420, 300, 780, 620)).resize((1080, 960), Image.LANCZOS)\
    .save(os.path.join(ROOT, '_c3_db_scar_zoom.jpg'), quality=95)

print('SCAR CHARACTER at +0.467s (frame 26)')
im = load(os.path.join(d3, fr[26])); L = lum(im); S = sat(im)
m = np.abs(im - plate).max(-1) > 12
sub = m[300:620, 420:780]
print('  effect px in crop %d, area %.3f cell^2' % (sub.sum(), sub.sum()/CELL**2))
px = im[m]
print('  mean RGB of the mass      = (%.0f, %.0f, %.0f)' % tuple(px.mean(0)))
print('  median luminance          = %.0f   (board floor 182; occluder target 40-110)' % np.median(L[m]))
print('  median saturation         = %.3f' % np.median(S[m]))
print('  hue: R-B = %+.0f  (positive = warm/brown, negative = cool)' % float((px[:, 0]-px[:, 2]).mean()))
print('  board plate mean RGB      = (%.0f, %.0f, %.0f), R-B %+.0f'
      % (*plate.reshape(-1, 3).mean(0), float((plate[..., 0]-plate[..., 2]).mean())))

print('\nIS IT A FLAT DECAL OR STANDING GEOMETRY? — seam continuity under the mass')
band = pl[300:700, :]
colmean = band.mean(0)
seams = [x for x in range(6, 1018) if colmean[x] == colmean[x-5:x+6].min()
         and colmean[x] < colmean[max(0, x-14):x+15].mean() - 3]
sc = []
for x in seams:
    if sc and x - sc[-1][-1] <= 3: sc[-1].append(x)
    else: sc.append([x])
sc = [int(np.mean(s)) for s in sc]
cov, cln = [], []
for x in sc:
    if x < 20 or x > 1000: continue
    for y in range(300, 640, 2):
        c = abs(float(pl[y, x]) - float(pl[y, max(0, x-14)]))
        if c < 20: continue
        obs = abs(float(L[y, x]) - float(L[y, max(0, x-14)]))
        if m[y, x] and m[y, max(0, x-14)]: cov.append(obs)
        elif not m[y, x]: cln.append(obs)
print('  clean seam contrast median %.1f (n=%d) | under the mass %.1f (n=%d) -> %s'
      % (np.median(cln), len(cln), np.median(cov) if cov else -1, len(cov),
         'fully opaque, hides the floor (reads as 3D matter, not a mark)' if cov and np.median(cov) < 8 else 'translucent mark'))

print('\nONSET — when does the tail mass appear relative to the impact?')
for i in range(19, 32):
    im2 = load(os.path.join(d3, fr[i]))
    m2 = np.abs(im2 - plate).max(-1) > 12
    reg = m2[300:640, 420:790]
    print('  %+0.3f   tail-region area %.3f cell^2' % ((i-T0)/FPS, reg.sum()/CELL**2))

print('\nDISSOLVE CHARACTER — is the fade-out a dither/stipple?')
from scipy import ndimage as ndi
for i in (18, 19, 20, 33, 34):
    im2 = load(os.path.join(d3, fr[i]))
    m2 = np.abs(im2 - plate).max(-1) > 12
    if m2.sum() < 50: continue
    lab, n = ndi.label(m2)
    sz = ndi.sum(m2, lab, range(1, n+1))
    print('  %+0.3f  %4d connected components, median size %.0f px, %d of them <60px  -> %s'
          % ((i-T0)/FPS, n, np.median(sz), int((sz < 60).sum()),
             'STIPPLED / dithered' if n > 60 and np.median(sz) < 60 else 'coherent'))
