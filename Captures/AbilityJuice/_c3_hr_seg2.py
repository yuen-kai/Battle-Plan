import numpy as np, json
from PIL import Image
from scipy import ndimage

P = {'r1': 'Captures/AbilityJuice/shots/r1/hitreact/f%04d.jpg',
     'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'}
Y0, Y1, X0, X1 = 330, 700, 150, 950
PX_PER_UNIT = 287.0 / 2.7          # tile pitch 287px, cell = 2.7 world units
PX_PER_CELL = 287.0


def parts(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    L = 0.2126*r + 0.7152*g + 0.0722*b
    green = (g - r > 34) & (g - b > 34)
    green = ndimage.binary_opening(green, np.ones((3, 3)))
    green = ndimage.binary_dilation(green, np.ones((5, 5)))     # bar + its dark casing
    nonboard = ((r - b) > -8)
    solid = nonboard & ~green
    solid = ndimage.binary_closing(solid, np.ones((3, 3)))
    solid = ndimage.binary_opening(solid, np.ones((3, 3)))
    # bridge the horizontal bar so the body is one blob
    bridged = ndimage.binary_closing(solid, np.ones((41, 3)))
    lab, n = ndimage.label(bridged)
    blobs = []
    for i in range(1, n+1):
        m = (lab == i) & solid
        s = int(m.sum())
        if s < 400:
            continue
        ys, xs = np.where(m)
        blobs.append(dict(n=s, m=m, cx=float(xs.mean()), cy=float(ys.mean()),
                          x0=int(xs.min()), x1=int(xs.max()), y0=int(ys.min()), y1=int(ys.max())))
    blobs.sort(key=lambda c: -c['n'])
    return blobs, green, L, solid


def analyse(tag, frames):
    out = []
    prev_cx = None
    for f in frames:
        a = np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)[Y0:Y1, X0:X1]
        blobs, green, L, solid = parts(a)
        if not blobs:
            continue
        # unit = the blob closest to the previous unit centre; seed on the largest at rest
        if prev_cx is None:
            unit = blobs[0]
        else:
            unit = min(blobs, key=lambda c: abs(c['cx'] - prev_cx) - 0.002*c['n'])
        prev_cx = unit['cx']
        stamps = [c for c in blobs if c is not unit]
        m = unit['m']
        Lu = L[m]
        mx = a.max(2); mn = a.min(2)
        sat = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0)
        val = mx/255.0
        gy, gx = np.where(green)
        # width along the impact axis measured as the median run length per row of the blob
        rows = [np.count_nonzero(m[y]) for y in range(m.shape[0]) if m[y].any()]
        rec = dict(f=f, t=round((f-12)/30.0, 4), n=int(m.sum()),
                   w=unit['x1']-unit['x0']+1, h=unit['y1']-unit['y0']+1,
                   cx=round(unit['cx']+X0, 1), cy=round(unit['cy']+Y0, 1),
                   L=round(float(Lu.mean()), 1),
                   f100=round(float((Lu < 100).mean()), 3), n100=int((Lu < 100).sum()),
                   f182=round(float((Lu < 182).mean()), 3), n182=int((Lu < 182).sum()),
                   f240=round(float((Lu > 240).mean()), 3), n240=int((Lu > 240).sum()),
                   sat=round(float(sat[m].mean()), 3), val=round(float(val[m].mean()), 3),
                   bar=round(float(gx.mean()+X0), 1) if len(gx) else None,
                   bary=round(float(gy.mean()+Y0), 1) if len(gy) else None,
                   stamp=[(c['n'], round(c['cx']+X0, 1), round(c['cy']+Y0, 1)) for c in stamps[:2]],
                   medrow=int(np.median(rows)))
        out.append(rec)
    return out


frames = list(range(0, 61))
res = {t: analyse(t, frames) for t in ('r2', 'r1')}
json.dump(res, open('Captures/AbilityJuice/_c3_hr_seg2.json', 'w'), indent=1)

for tag in ('r2', 'r1'):
    print(f'\n===== {tag} =====   1 cell = {PX_PER_CELL:.0f}px, 1 world unit = {PX_PER_UNIT:.1f}px')
    print(f"{'f':>3} {'t':>7} {'area':>6} {'A/rest':>6} {'w':>4} {'h':>4} {'w/rest':>6} {'cx':>7} "
          f"{'dx_cell':>7} {'L':>6} {'n<100':>6} {'n<182':>6} {'n>240':>6} {'sat':>5} {'bar cx':>7} {'bar-unit':>8}")
    rows = res[tag]
    rest = rows[0]
    for x in rows:
        if x['f'] > 46 and x['f'] % 5:
            continue
        print(f"{x['f']:3d} {x['t']:+7.3f} {x['n']:6d} {x['n']/rest['n']:6.2f} {x['w']:4d} {x['h']:4d} "
              f"{x['w']/rest['w']:6.2f} {x['cx']:7.1f} {(x['cx']-rest['cx'])/PX_PER_CELL:+7.3f} "
              f"{x['L']:6.1f} {x['n100']:6d} {x['n182']:6d} {x['n240']:6d} {x['sat']:5.3f} "
              f"{x['bar']:7.1f} {x['bar']-x['cx']:+8.1f}   {x['stamp']}")
