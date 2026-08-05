import numpy as np, os
from PIL import Image, ImageDraw
from scipy import ndimage as ndi
from _c3_db_setup import ROOT, FPS, T0, load, lum, sat, plate_of

CELL = 199.0
d3 = os.path.join(ROOT, 'shots/r3/debris')
d2 = os.path.join(ROOT, 'shots/r2/debris')

def blobs(d, i, minpx=120):
    plate, fr = plate_of(d)
    pl = lum(plate)
    im = load(os.path.join(d, fr[i])); L = lum(im)
    m = np.abs(im - plate).max(-1) > 12
    lab, n = ndi.label(m)
    sz = ndi.sum(m, lab, range(1, n+1)); objs = ndi.find_objects(lab)
    out = []
    for k, s in enumerate(sz):
        if s < minpx: continue
        sl = objs[k]
        w = (sl[1].stop - sl[1].start); h = (sl[0].stop - sl[0].start)
        sub = (lab[sl] == k+1)
        Lsub = L[sl][sub]
        out.append(dict(px=int(s), w=w, h=h, wc=w/CELL, hc=h/CELL,
                        area=s/CELL**2, fill=s/max(w*h, 1),
                        aspect=max(w, h)/max(min(w, h), 1),
                        lmed=float(np.median(Lsub)),
                        lspread=float(np.percentile(Lsub, 90) - np.percentile(Lsub, 10)),
                        lmin=float(Lsub.min()),
                        cx=(sl[1].start+sl[1].stop)//2, cy=(sl[0].start+sl[0].stop)//2,
                        sl=sl))
    out.sort(key=lambda b: -b['px'])
    return out, im, m, pl

print('=' * 112)
print('AFTERMATH PANEL (frame 28, +0.533s) — what is actually on the board')
for nm, d in (('R2', d2), ('R3', d3)):
    bs, im, m, pl = blobs(d, 28)
    tot = sum(b['area'] for b in bs)
    print('\n %s  — %d blobs >=120px, total covered %.3f cell^2, effect px %d' % (nm, len(bs), tot, int(m.sum())))
    print('   #   px     w x h (cells)   fill%%  aspect  Lmed  Lspread  Lmin   role-guess')
    for k, b in enumerate(bs[:12]):
        guess = 'flat disc' if b['lspread'] < 18 and b['aspect'] < 1.5 else (
                'shaded/irregular' if b['lspread'] >= 30 else 'low-spread')
        print('  %2d  %5d   %.2f x %.2f     %3.0f    %.2f   %5.1f   %5.1f  %5.1f   %s'
              % (k, b['px'], b['wc'], b['hc'], 100*b['fill'], b['aspect'], b['lmed'], b['lspread'], b['lmin'], guess))
    d_ = [max(b['wc'], b['hc']) for b in bs]
    if d_:
        print('   size hierarchy: largest %.2f  median %.2f  ratio %.1f:1' % (d_[0], np.median(d_), d_[0]/max(np.median(d_), 1e-6)))
        print('   mean internal luminance spread across blobs: %.1f   (Clash Mini puffs 30-45)'
              % np.mean([b['lspread'] for b in bs]))

print()
print('=' * 112)
print('GROUND SCARS — isolate flat-on-floor marks (no lift, low value) across the tail')
plate, fr = plate_of(d3); pl = lum(plate)
for i in (20, 22, 24, 26, 28, 30, 32):
    im = load(os.path.join(d3, fr[i])); L = lum(im); S = sat(im)
    m = np.abs(im - plate).max(-1) > 12
    lab, n = ndi.label(m)
    sz = ndi.sum(m, lab, range(1, n+1)); objs = ndi.find_objects(lab)
    big = [(k+1, s) for k, s in enumerate(sz) if s >= 400]
    print('  %+0.3f  %2d blobs>=400px  ' % ((i-T0)/FPS, len(big)), end='')
    desc = []
    for k, s in big[:8]:
        sl = objs[k-1]; sub = (lab[sl] == k)
        Ls = L[sl][sub]
        desc.append('%.2fx%.2fc L%.0f sp%.0f' % ((sl[1].stop-sl[1].start)/CELL, (sl[0].stop-sl[0].start)/CELL,
                                                 np.median(Ls), np.percentile(Ls, 90)-np.percentile(Ls, 10)))
    print(' | '.join(desc))

# render aftermath panels big, separately
for nm, d, i in (('r2_after', d2, 28), ('r3_after', d3, 28), ('r3_t33', d3, 22), ('r3_t40', d3, 24)):
    Image.open(os.path.join(d, sorted(os.listdir(d))[i])).convert('RGB').save(
        os.path.join(ROOT, '_c3_db_%s.jpg' % nm), quality=94)
print('\npanels written')
