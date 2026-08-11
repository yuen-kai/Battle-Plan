import numpy as np, os
from PIL import Image, ImageDraw
from _c3_db_setup import ROOT, FPS, T0, load, lum, plate_of

CELL = 199.0
d3 = os.path.join(ROOT, 'shots/r3/debris')
d2 = os.path.join(ROOT, 'shots/r2/debris')

def bbox_of(d, idxs):
    plate, fr = plate_of(d)
    ys, xs = [], []
    for i in idxs:
        im = load(os.path.join(d, fr[i]))
        m = np.abs(im - plate).max(-1) > 12
        if m.sum() < 100: continue
        y, x = np.nonzero(m)
        ys += [y.min(), y.max()]; xs += [x.min(), x.max()]
    return min(ys), max(ys), min(xs), max(xs), fr

# --- impact window zoom, r3
y0, y1, x0, x1 = 250, 700, 300, 780
idxs = [12, 13, 14, 15, 16]
tiles = []
_, fr = plate_of(d3)
for i in idxs:
    im = Image.open(os.path.join(d3, fr[i])).convert('RGB').crop((x0, y0, x1, y1)).resize((480, 450), Image.LANCZOS)
    dr = ImageDraw.Draw(im); dr.text((8, 8), '%+0.3fs' % ((i-T0)/FPS), fill=(255, 255, 0))
    tiles.append(im)
sheet = Image.new('RGB', (480*len(tiles), 450))
for k, t in enumerate(tiles): sheet.paste(t, (480*k, 0))
sheet.save(os.path.join(ROOT, '_c3_db_impact_row.jpg'), quality=92)
print('impact row ->', sheet.size)

# --- money frame big
for i, nm in ((14, 'money067'), (15, 'panel100')):
    im = Image.open(os.path.join(d3, fr[i])).convert('RGB').crop((330, 280, 780, 680)).resize((900, 800), Image.LANCZOS)
    im.save(os.path.join(ROOT, '_c3_db_%s.jpg' % nm), quality=94)
print('big frames written')

# --- aftermath: r3 vs r2 at their shipped aftermath frames
_, fr2 = plate_of(d2)
a3 = Image.open(os.path.join(d3, fr[28])).convert('RGB')
a2 = Image.open(os.path.join(d2, fr2[28])).convert('RGB')
cmp = Image.new('RGB', (2048, 1024))
cmp.paste(a2, (0, 0)); cmp.paste(a3, (1024, 0))
dr = ImageDraw.Draw(cmp)
dr.text((20, 20), 'R2 aftermath +0.533s', fill=(255, 255, 0))
dr.text((1044, 20), 'R3 aftermath +0.533s', fill=(255, 255, 0))
cmp.resize((1400, 700), Image.LANCZOS).save(os.path.join(ROOT, '_c3_db_after_cmp.jpg'), quality=92)
print('aftermath compare written')
