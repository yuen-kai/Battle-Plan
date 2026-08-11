"""How much does CLASH MINI's camera actually move at impact?

Same similarity solve, run between panel 1 (wind-up) and panel 2 (impact) of each
reference strip, then panel 2 -> panel 3. Normalised to our 1024px frame height so the
numbers are directly comparable to ours.
"""
import numpy as np
from PIL import Image
from scipy import ndimage, optimize
import os, glob, json

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"
REF = os.path.join(AJ, "strips/reference")
RNG = np.random.default_rng(3)


def panels(path):
    im = Image.open(path).convert("L")
    W, H = im.size
    gut = (W - 3 * H) // 2
    a = np.asarray(im, dtype=np.float64)
    return [a[:, 0:H], a[:, H + gut:2 * H + gut], a[:, W - H:W]], H


class Al:
    def __init__(self, ref, mask, S, npts=45000):
        ys, xs = np.nonzero(mask)
        if len(ys) > npts:
            k = RNG.choice(len(ys), npts, replace=False); ys, xs = ys[k], xs[k]
        self.c = S / 2.0
        self.py = ys - self.c
        self.px = xs - self.c
        self.rv = ref[ys, xs]

    def cost(self, mov, p):
        tx, ty, roll, zoom = p
        th = np.radians(roll); s = 1 + zoom
        c, si = np.cos(th) * s, np.sin(th) * s
        mx = c * self.px - si * self.py + tx + self.c
        my = si * self.px + c * self.py + ty + self.c
        v = ndimage.map_coordinates(mov, [my, mx], order=1, mode="nearest")
        return float(np.abs(v - self.rv).mean())


def solve(al, mov):
    f = lambda p: al.cost(mov, p)
    cands = [np.zeros(4)]
    for _ in range(40):
        cands.append(RNG.normal(0, [14, 14, 1.2, 0.020]))
    best = min(((f(c), c) for c in cands), key=lambda t: t[0])
    for sc in ([6, 6, 0.6, 0.010], [2, 2, 0.2, 0.003], [0.5, 0.5, 0.05, 0.0008],
               [0.12, 0.12, 0.012, 0.0002]):
        simp = np.vstack([best[1]] + [best[1] + np.eye(4)[i] * sc[i] for i in range(4)])
        r = optimize.minimize(f, best[1], method="Nelder-Mead",
                              options=dict(xatol=1e-5, fatol=1e-7, maxiter=2000,
                                           initial_simplex=simp))
        if r.fun < best[0]:
            best = (r.fun, r.x)
    return best[1], best[0]


def edge_mask(img, S, effect_center=None):
    """Use the board framing/furniture, exclude the bright effect blowout."""
    m = np.ones((S, S), bool)
    b = int(S * 0.06)
    m[:b, :] = False; m[-b:, :] = False; m[:, :b] = False; m[:, -b:] = False
    return m


print("=" * 124)
print("CLASH MINI reference strips — camera motion between the frames a stranger compares")
print("(normalised to a 1024px-tall frame so it is directly comparable to our capture)")
print("=" * 124)
print(f"{'strip':>28} {'pair':>7} | {'|T| px':>8} {'%frame':>7} | {'roll°':>7} {'zoom%':>7} | "
      f"{'cornRoll':>8} {'zoomPx':>7} {'rotShr':>7} {'cornTot':>8} | {'SAD':>6}")
rows = []
for p in sorted(glob.glob(os.path.join(REF, "*.jpg"))):
    pans, S = panels(p)
    k = 1024.0 / S
    R = np.hypot(512.0, 512.0)
    for (i, j), tag in (((0, 1), "1->2"), ((1, 2), "2->3")):
        a, b = pans[i], pans[j]
        # skip pairs where a huge white blowout dominates: mask very bright pixels
        m = edge_mask(a, S)
        m &= (a < 235)
        if m.mean() < 0.25:
            continue
        al = Al(a, m, S)
        par, sad = solve(al, b)
        tx, ty, roll, zoom = par
        mag = np.hypot(tx, ty) * k
        cr = abs(np.radians(roll)) * R
        zp = abs(zoom) * R
        share = cr / (cr + mag) if (cr + mag) > 1e-9 else 0
        rows.append(dict(strip=os.path.basename(p), pair=tag, mag=mag, roll=roll,
                         zoom=zoom * 100, cr=cr, zp=zp, share=share,
                         tot=mag + cr + zp, sad=sad))
        print(f"{os.path.basename(p)[:-4]:>28} {tag:>7} | {mag:8.2f} {100*mag/1024:6.2f}% | "
              f"{roll:7.3f} {zoom*100:7.3f} | {cr:8.2f} {zp:7.2f} {100*share:6.1f}% "
              f"{mag+cr+zp:8.2f} | {sad:6.2f}")

ok = [r for r in rows if r["sad"] < 22]
print(f"\n{len(ok)}/{len(rows)} pairs converged (SAD < 22). Summary over those:")
for key, lab in (("mag", "translation px"), ("roll", "roll deg"),
                 ("zoom", "zoom %"), ("tot", "total corner px"), ("share", "rot share")):
    v = np.array([abs(r[key]) for r in ok])
    print(f"    {lab:>18}: median {np.median(v):8.3f}   p75 {np.percentile(v,75):8.3f}   "
          f"max {v.max():8.3f}")
json.dump(rows, open(os.path.join(AJ, "_r2_cam_cm.json"), "w"), indent=1)
