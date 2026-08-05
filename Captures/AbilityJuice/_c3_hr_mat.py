import numpy as np, json
from PIL import Image
from scipy import ndimage

P = {'r1': 'Captures/AbilityJuice/shots/r1/hitreact/f%04d.jpg',
     'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'}
BOX = (380, 370, 880, 650)
PX_CELL = 287.0


def load(tag, f):
    a = np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)
    return a[BOX[1]:BOX[3], BOX[0]:BOX[2]]


def classify(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    L = 0.2126*r + 0.7152*g + 0.0722*b
    mx, mn = a.max(2), a.min(2)
    S = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0)
    V = mx/255.0
    green = (g - r > 34) & (g - b > 34)
    green = ndimage.binary_dilation(ndimage.binary_opening(green, np.ones((3, 3))), np.ones((7, 7)))
    rim = (L < 60) & ~green
    body = (r - b > 50) & ~green
    gun = (r > g+3) & (g > b+3) & (L >= 60) & (L < 185) & (r - b <= 50) & ~green
    white = (L > 235) & (S < 0.18) & ~green
    unit = rim | body | gun | white
    unit = ndimage.binary_opening(unit, np.ones((3, 3)))
    unit = ndimage.binary_closing(unit, np.ones((5, 5)))
    lab, n = ndimage.label(ndimage.binary_closing(unit, np.ones((45, 5))))
    keep = np.zeros_like(unit)
    best = None
    for i in range(1, n+1):
        m = (lab == i) & unit
        if m.sum() > (best[0] if best else 400):
            best = (int(m.sum()), m)
    if best:
        keep = best[1]
    return dict(L=L, S=S, V=V, rim=rim & keep, body=body & keep, gun=gun & keep,
                white=white & keep, unit=keep, green=green)


def report(tag, frames):
    print(f'\n===== {tag} =====')
    print(f"{'f':>3} {'t':>7} | {'unit':>6} {'A/rest':>6} {'w':>4} {'h':>4} {'w/r':>5} {'h/r':>5} "
          f"{'cx':>6} {'dxcell':>7} | {'rim':>6} {'r/rest':>6} {'body':>6} {'gun':>5} {'white':>6} | "
          f"{'Lbody':>6} {'Sbody':>6} {'Vbody':>6} | {'Lrim':>5} {'Swhite':>6}")
    rest = None
    for f in frames:
        a = load(tag, f)
        c = classify(a)
        u = c['unit']
        ys, xs = np.where(u)
        if not len(ys):
            continue
        row = dict(n=int(u.sum()), w=int(xs.max()-xs.min()+1), h=int(ys.max()-ys.min()+1),
                   cx=float(xs.mean())+BOX[0],
                   rim=int(c['rim'].sum()), body=int(c['body'].sum()),
                   gun=int(c['gun'].sum()), white=int(c['white'].sum()),
                   Lbody=float(c['L'][c['body']].mean()) if c['body'].sum() else 0,
                   Sbody=float(c['S'][c['body']].mean()) if c['body'].sum() else 0,
                   Vbody=float(c['V'][c['body']].mean()) if c['body'].sum() else 0,
                   Lrim=float(c['L'][c['rim']].mean()) if c['rim'].sum() else 0,
                   Swhite=float(c['S'][c['white']].mean()) if c['white'].sum() else 0)
        if rest is None:
            rest = row
        print(f"{f:3d} {(f-12)/30.0:+7.3f} | {row['n']:6d} {row['n']/rest['n']:6.2f} {row['w']:4d} "
              f"{row['h']:4d} {row['w']/rest['w']:5.2f} {row['h']/rest['h']:5.2f} {row['cx']:6.1f} "
              f"{(row['cx']-rest['cx'])/PX_CELL:+7.3f} | {row['rim']:6d} {row['rim']/max(rest['rim'],1):6.2f} "
              f"{row['body']:6d} {row['gun']:5d} {row['white']:6d} | {row['Lbody']:6.1f} {row['Sbody']:6.3f} "
              f"{row['Vbody']:6.3f} | {row['Lrim']:5.1f} {row['Swhite']:6.3f}")


fr = [8] + list(range(12, 32)) + [36, 40, 42, 43, 50]
report('r2', fr)
report('r1', fr)

# --- bloom halo: how much does the floor lift near the unit at the flash frame? ---
print('\n--- floor lift from bloom (r2) ---')
base = np.asarray(Image.open(P['r2'] % 8).convert('RGB')).astype(np.float32)
for f in (12, 13, 14, 15):
    a = np.asarray(Image.open(P['r2'] % f).convert('RGB')).astype(np.float32)
    d = (0.2126*a[..., 0]+0.7152*a[..., 1]+0.0722*a[..., 2]) - \
        (0.2126*base[..., 0]+0.7152*base[..., 1]+0.0722*base[..., 2])
    # ring of clean floor well outside the unit at both positions
    ring = np.zeros(d.shape, bool)
    ring[250:330, 400:900] = True      # above
    ring[640:720, 400:900] = True      # below
    print(f'f{f}: floor delta mean {d[ring].mean():+.2f}  p95 {np.percentile(d[ring],95):+.2f} '
          f'max {d[ring].max():+.1f}  n(>8) {int((d[ring]>8).sum())}/{int(ring.sum())}')
