import numpy as np, json
from PIL import Image
from scipy import ndimage

P = {'r1': 'Captures/AbilityJuice/shots/r1/hitreact/f%04d.jpg',
     'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'}
Y0, Y1, X0, X1 = 330, 700, 200, 900


def comps(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    L = 0.2126*r + 0.7152*g + 0.0722*b
    green = (g - r > 30) & (g - b > 30)
    nonboard = ((r - b) > -8) & ~green
    nonboard = ndimage.binary_closing(nonboard, np.ones((3, 3)))
    nonboard = ndimage.binary_opening(nonboard, np.ones((3, 3)))
    lab, n = ndimage.label(nonboard)
    out = []
    for i in range(1, n+1):
        m = lab == i
        s = int(m.sum())
        if s < 300:
            continue
        ys, xs = np.where(m)
        out.append(dict(n=s, m=m, cx=float(xs.mean()), cy=float(ys.mean()),
                        x0=int(xs.min()), x1=int(xs.max()), y0=int(ys.min()), y1=int(ys.max())))
    out.sort(key=lambda c: -c['n'])
    return out, green, L


def scale_px_per_unit():
    """Tile pitch from the floor's vertical seams: one cell = 2.7 world units."""
    a = np.asarray(Image.open(P['r2'] % 8).convert('RGB')).astype(np.float32)
    row = a[720:760, 100:950].mean((0, 2))          # clean floor band below the unit
    d = np.abs(np.diff(row))
    peaks = [i for i in range(1, len(d)-1) if d[i] > 3 and d[i] >= d[i-1] and d[i] > d[i+1]]
    merged = []
    for p in peaks:
        if merged and p - merged[-1] < 12:
            continue
        merged.append(p)
    gaps = np.diff(merged)
    return merged, gaps


print('seam x (offset 100):', scale_px_per_unit()[0])
print('gaps:', scale_px_per_unit()[1])

for tag in ('r2', 'r1'):
    print(f'\n===== {tag} =====')
    print(f"{'f':>3} {'t':>7} | {'unit n':>7} {'w':>4} {'h':>4} {'cx':>7} {'cy':>7} {'L':>6} "
          f"{'<100':>6} {'<182':>6} {'>240':>6} {'sat':>5} | {'bar cx':>7} {'bar cy':>6} | others")
    for f in list(range(6, 32)) + [40, 45, 55]:
        a = np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)[Y0:Y1, X0:X1]
        cs, green, L = comps(a)
        if not cs:
            continue
        unit = cs[0]
        others = [(c['n'], round(c['cx'], 1), round(c['cy'], 1)) for c in cs[1:4]]
        m = unit['m']
        Lu = L[m]
        mx = a.max(2); mn = a.min(2)
        sat = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0)[m]
        gy, gx = np.where(green)
        print(f"{f:3d} {(f-12)/30.0:+7.3f} | {unit['n']:7d} {unit['x1']-unit['x0']+1:4d} "
              f"{unit['y1']-unit['y0']+1:4d} {unit['cx']+X0:7.1f} {unit['cy']+Y0:7.1f} {Lu.mean():6.1f} "
              f"{(Lu<100).mean():6.3f} {(Lu<182).mean():6.3f} {(Lu>240).mean():6.3f} {sat.mean():5.3f} | "
              f"{gx.mean()+X0:7.1f} {gy.mean()+Y0:6.1f} | {others}")
