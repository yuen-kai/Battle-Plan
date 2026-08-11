import numpy as np, os
from PIL import Image, ImageDraw
from _c3_db_setup import ROOT, FPS, T0, load, lum, plate_of

CELL = 199.0
d3 = os.path.join(ROOT, 'shots/r3/debris')
plate, fr = plate_of(d3)
pl = lum(plate)

idxs = [17, 19, 21, 23, 25, 27, 29, 31]
tiles = []
for i in idxs:
    im = Image.open(os.path.join(d3, fr[i])).convert('RGB').crop((330, 230, 780, 680)).resize((360, 360), Image.LANCZOS)
    ImageDraw.Draw(im).text((6, 6), '%+0.3fs' % ((i-T0)/FPS), fill=(255, 255, 0))
    tiles.append(im)
sheet = Image.new('RGB', (360*4, 360*2))
for k, t in enumerate(tiles):
    sheet.paste(t, (360*(k % 4), 360*(k//4)))
sheet.save(os.path.join(ROOT, '_c3_db_tail.jpg'), quality=93)
print('tail sheet', sheet.size)

# centroid + bbox drift of the settled mass
print('\n   t     centroid(x,y)   drift/frame(cells)   bbox(cells)   area cell^2   darkest')
prev = None
for i in range(17, 34):
    im = load(os.path.join(d3, fr[i])); L = lum(im)
    m = np.abs(im - plate).max(-1) > 12
    if m.sum() < 200:
        print('  %+0.3f   (empty)' % ((i-T0)/FPS)); prev = None; continue
    y, x = np.nonzero(m)
    cx, cy = x.mean(), y.mean()
    dr = 0.0 if prev is None else np.hypot(cx-prev[0], cy-prev[1])/CELL
    print('  %+0.3f   (%4.0f,%4.0f)      %.4f            %.2f x %.2f    %.3f       %.0f'
          % ((i-T0)/FPS, cx, cy, dr, (x.max()-x.min())/CELL, (y.max()-y.min())/CELL,
             m.sum()/CELL**2, L[m].min()))
    prev = (cx, cy)
