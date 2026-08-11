import numpy as np, json
from PIL import Image
from scipy import ndimage

P2 = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
P1 = 'Captures/AbilityJuice/shots/r1/hitreact/f%04d.jpg'
# crop that holds the unit + health bar through the whole knockback, no obstacles
Y0, Y1, X0, X1 = 360, 680, 300, 800


def load(path, f):
    a = np.asarray(Image.open(path % f).convert('RGB')).astype(np.float32)
    return a[Y0:Y1, X0:X1]


def masks(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    L = 0.2126*r + 0.7152*g + 0.0722*b
    rb = r - b
    raw = rb > -8
    raw = ndimage.binary_opening(raw, np.ones((3, 3)))
    lab, n = ndimage.label(raw)
    if n == 0:
        return np.zeros_like(raw), np.zeros_like(raw), L
    sizes = ndimage.sum(raw, lab, range(1, n+1))
    order = np.argsort(sizes)[::-1]
    # green health bar: g clearly above r and b
    green = (g - r > 30) & (g - b > 30)
    comps = [(int(sizes[i]), lab == i+1) for i in order[:6]]
    return comps, green, L


def describe(tag, path, frames):
    out = []
    for f in frames:
        a = load(path, f)
        comps, green, L = masks(a)
        # unit = biggest non-green component
        unit = None
        for size, m in comps:
            if (m & green).sum() > 0.4 * m.sum():
                continue
            unit = m
            break
        # merge any component touching the unit bbox (gun tip etc.) that isn't green
        allm = np.zeros_like(unit)
        for size, m in comps:
            if size < 150:
                continue
            if (m & green).sum() > 0.4 * m.sum():
                continue
            allm |= m
        unit = allm
        ys, xs = np.where(unit)
        gy, gx = np.where(green)
        r, g, b = a[..., 0], a[..., 1], a[..., 2]
        mx = a.max(2); mn = a.min(2)
        sat = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0)
        val = mx/255.0
        Lu = L[unit]
        rec = dict(
            f=f, t=round((f-12)/30.0, 4),
            n=int(unit.sum()),
            x0=int(xs.min()), x1=int(xs.max()), y0=int(ys.min()), y1=int(ys.max()),
            w=int(xs.max()-xs.min()+1), h=int(ys.max()-ys.min()+1),
            cx=round(float(xs.mean()), 2), cy=round(float(ys.mean()), 2),
            Lmean=round(float(Lu.mean()), 1),
            dark60=round(float((Lu < 60).mean()), 4),
            dark100=round(float((Lu < 100).mean()), 4),
            dark182=round(float((Lu < 182).mean()), 4),
            bright240=round(float((Lu > 240).mean()), 4),
            sat=round(float(sat[unit].mean()), 3),
            satp90=round(float(np.percentile(sat[unit], 90)), 3),
            val=round(float(val[unit].mean()), 3),
            gcx=round(float(gx.mean()), 2) if len(gx) else None,
            gcy=round(float(gy.mean()), 2) if len(gy) else None,
            gn=int(green.sum()),
        )
        out.append(rec)
    return out


frames = list(range(0, 61))
r2 = describe('r2', P2, frames)
r1 = describe('r1', P1, frames)
json.dump(dict(r1=r1, r2=r2), open('Captures/AbilityJuice/_c3_hr_mask.json', 'w'), indent=1)

hdr = f"{'f':>3} {'t':>7} {'n':>6} {'w':>4} {'h':>4} {'cx':>7} {'cy':>7} {'Lmn':>6} {'d60':>6} {'d100':>6} {'d182':>6} {'br240':>6} {'sat':>5} {'val':>5} {'gcx':>7} {'gcy':>7}"
for tag, rows in (('R2', r2), ('R1', r1)):
    print(f'=== {tag} ===')
    print(hdr)
    for x in rows:
        print(f"{x['f']:3d} {x['t']:+7.3f} {x['n']:6d} {x['w']:4d} {x['h']:4d} {x['cx']:7.2f} {x['cy']:7.2f} "
              f"{x['Lmean']:6.1f} {x['dark60']:6.3f} {x['dark100']:6.3f} {x['dark182']:6.3f} {x['bright240']:6.3f} "
              f"{x['sat']:5.3f} {x['val']:5.3f} {x['gcx'] if x['gcx'] is not None else -1:7.2f} {x['gcy'] if x['gcy'] is not None else -1:7.2f}")
